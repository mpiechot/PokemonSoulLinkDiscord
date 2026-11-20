-- Read-only Ruby reference bridge. No emu:write*, save, input or remote command path exists.
-- Sources and AXVD validation limits: docs/soullink/mgba.md.
-- The Windows player loads this script automatically. For manual use, start the local client first.
-- Override only with a layout verified for your ROM before loading this file:
-- SOULLINK_PROFILE = {id="my-verified-profile", party=..., partyCount=..., storage=..., save1=..., save2=..., main=...}
local profiles = {
    AXVD = {
        title = "POKEMON RUBY", revisions = {[0] = true, [1] = true},
        id = "ruby-axvd-reference-v2",
        party = 0x03004370, partyCount = 0x03004360,
        storage = 0x020300A0, save1 = 0x02025734, save2 = 0x02024EA4, main = 0x03001770,
        stats = 0x0820BBE8, trainers = 0x081FD478
    }
}
local port = SOULLINK_PORT or 5091
local peer, pending, position = nil, nil, 1
local epoch = tostring({}):gsub("[^%w]", "")
local tick, lastFrame = 0, -1
local lastError = ""

-- Resolve the core for each read because the newer bindings return a short-lived reference.
local function readCore()
    local ok, core = pcall(function() return emu._core end)
    if ok and core then return core end
    return emu
end

local function read8(address)
    return readCore():read8(address)
end

local function read16(address)
    return readCore():read16(address)
end

local function readRange(address, size)
    return readCore():readRange(address, size)
end

local function read32(address)
    return readCore():read32(address)
end

local function stop()
    if peer then pcall(function() peer:close() end) end
    peer, pending, position = nil, nil, 1
end

local function report(message)
    if message ~= lastError then console:log("SoulLink: " .. message); lastError = message end
end

local function hex(address, size)
    return (readRange(address, size):gsub(".", function(c) return string.format("%02X", string.byte(c)) end))
end

local function readTypes(profile)
    if not profile.stats then return "" end
    local stats = readRange(profile.stats, 412 * 28)
    if #stats ~= 412 * 28 or stats:sub(1, 28) ~= string.rep("\0", 28) then return "" end
    local result = {}
    for species = 0, 411 do
        local one, two = stats:byte(species * 28 + 7, species * 28 + 8)
        local hp = stats:byte(species * 28 + 1)
        result[species + 1] = hp == 0 and "FFFF" or string.format("%02X%02X", one, two)
    end
    return table.concat(result)
end

local function readNextGym(profile, badges)
    if not profile.trainers then return "null" end
    for gym = 0, 7 do
        if (badges & (1 << gym)) == 0 then
            local trainer = profile.trainers + (265 + gym) * 40
            local flags = read8(trainer)
            local count = read8(trainer + 0x20)
            local party = read32(trainer + 0x24)
            local stride = (flags & 1) == 0 and 8 or 16
            if flags > 3 or count < 1 or count > 6 or read8(trainer + 1) ~= 25 or
                read8(trainer + 3) ~= 71 + gym or party < 0x08000000 or party + count * stride > 0x0A000000 then
                return "null"
            end
            local levels = {}
            for slot = 0, count - 1 do
                local level = read16(party + slot * stride + 2)
                if level < 1 or level > 100 then return "null" end
                levels[slot + 1] = level
            end
            return "[" .. table.concat(levels, ",") .. "]"
        end
    end
    return "null"
end

local function sendPending()
    if not peer or not pending then return end
    local index, err = peer:send(pending, position)
    if index then
        position = index + 1
        if position > #pending then pending, position = nil, 1 end
    elseif err ~= socket.ERRORS.AGAIN then
        stop()
    end
end

local function snapshot(frame)
    -- Header identity tolerates randomization; structural checks reject invalid memory reads.
    local code = readRange(0x080000AC, 4)
    local title = readRange(0x080000A0, 12):gsub("%z", "")
    local reference = profiles[code]
    local revision = read8(0x080000BC)
    if not reference or title:gsub("%s+$", "") ~= reference.title or not reference.revisions[revision] then
        error(string.format("No read-only profile for ROM %q revision %d.", code, revision), 0)
    end
    local profile = SOULLINK_PROFILE or reference
    local count = read8(profile.partyCount)
    local currentBox = read8(profile.storage)
    local hours = read16(profile.save2 + 0x0E)
    local minutes = read8(profile.save2 + 0x10)
    local seconds = read8(profile.save2 + 0x11)
    if count > 6 or currentBox > 13 or hours > 999 or minutes > 59 or seconds > 59 then
        error(string.format("Invalid %s memory layout: party=%d, box=%d, play time=%d:%02d:%02d.",
            code, count, currentBox, hours, minutes, seconds), 0)
    end
    if count == 0 then
        error("Waiting for a loaded game with at least one party Pokémon.", 0)
    end
    local pokemon = {}
    for slot = 0, count - 1 do
        table.insert(pokemon, string.format('{"hex":"%s","container":"Party","slot":%d}', hex(profile.party + slot * 100, 100), slot))
    end
    -- Every snapshot covers all 14 boxes. A temporary partial read must never look like a release.
    for box = 0, 13 do
        for slot = 0, 29 do
            local address = profile.storage + 4 + (box * 30 + slot) * 80
            local flags = read8(address + 19)
            if (flags & 2) ~= 0 then
                table.insert(pokemon, string.format('{"hex":"%s","container":"Box","slot":%d,"box":%d}', hex(address, 80), slot, box))
            end
        end
    end
    local battle = (read8(profile.main + 0x43D) & 2) ~= 0
    -- Badges are eight consecutive flags starting at FLAG_BADGE01_GET (0x807).
    local badges = (read16(profile.save1 + 0x1220 + 0x100) >> 7) & 0xFF
    local gymOk, gym = pcall(readNextGym, profile, badges)
    local typesOk, types = pcall(readTypes, profile)
    return string.format('{"version":1,"type":"snapshot","gameCode":"%s","layoutId":"%s","epoch":"%s","frame":%d,"playSeconds":%d,"mapGroup":%d,"mapNumber":%d,"area":255,"inBattle":%s,"badgeMask":%d,"gymLevels":%s,"rubyTypesHex":"%s","completeInventory":true,"pokemon":[%s]}\n',
        code, profile.id, epoch, frame, hours * 3600 + minutes * 60 + seconds,
        read8(profile.save1 + 4), read8(profile.save1 + 5), tostring(battle), badges,
        gymOk and gym or "null", typesOk and types or "", table.concat(pokemon, ","))
end

local function onFrame()
    tick = tick + 1
    local frame = readCore():currentFrame()
    if frame < lastFrame then epoch = tostring({}):gsub("[^%w]", "") end
    lastFrame = frame
    sendPending()
    if tick % 60 ~= 0 or pending then return end
    if not peer then
        peer = socket.tcp()
        peer:add("error", function() stop() end)
        if not peer:connect("127.0.0.1", port) then stop(); report("Local client unavailable; retrying."); return end
    end
    local ok, result = pcall(snapshot, frame)
    if not ok then report(tostring(result)); return end
    pending, position = result, 1
    sendPending()
    report("Streaming read-only reference profile. Check party and box contents in the client before starting the run.")
end

-- Re-loading this file removes only callbacks installed by its previous instance.
if SOULLINK_STOP then SOULLINK_STOP() end
local frameCallback = callbacks:add("frame", onFrame)
local resetCallback = callbacks:add("reset", function() epoch = tostring({}):gsub("[^%w]", ""); stop() end)
local loadCallback = callbacks:add("loadState", function() epoch = tostring({}):gsub("[^%w]", ""); stop() end)
SOULLINK_STOP = function()
    callbacks:remove(frameCallback)
    callbacks:remove(resetCallback)
    callbacks:remove(loadCallback)
    stop()
end
console:log("SoulLink read-only bridge loaded. Reference profile requires live validation; no game memory is modified.")
