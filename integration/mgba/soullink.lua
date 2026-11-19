-- Read-only Ruby reference bridge. No emu:write*, save, input or remote command path exists.
-- Sources and AXVD validation limits: docs/soullink/mgba.md.
-- Start the local C# client first, load a save, then load this script in Tools > Scripting.
-- Override only with a layout verified for your ROM before loading this file:
-- SOULLINK_PROFILE = {id="my-verified-profile", party=..., partyCount=..., storage=..., save1=..., save2=..., main=...}
local profile = SOULLINK_PROFILE or {
    id = "ruby-axvd-reference-v1",
    party = 0x03004360, partyCount = 0x03004350,
    storage = 0x020300A0, save1 = 0x02025734, save2 = 0x02024EA4, main = 0x03001770
}
local port = SOULLINK_PORT or 5091
local peer, pending, position = nil, nil, 1
local epoch = tostring({}):gsub("[^%w]", "")
local tick, lastFrame = 0, -1
local lastError = ""

local function stop()
    if peer then pcall(function() peer:close() end) end
    peer, pending, position = nil, nil, 1
end

local function report(message)
    if message ~= lastError then console:log("SoulLink: " .. message); lastError = message end
end

local function hex(address, size)
    return (emu:readRange(address, size):gsub(".", function(c) return string.format("%02X", string.byte(c)) end))
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
    local code = emu:readRange(0x080000AC, 4)
    local title = emu:readRange(0x080000A0, 12):gsub("%z", "")
    if code ~= "AXVD" or title:gsub("%s+$", "") ~= "POKEMON RUBY" then
        error("Unsupported ROM. Only German Ruby AXVD is allowed.")
    end
    local count = emu:read8(profile.partyCount)
    local currentBox = emu:read8(profile.storage)
    local hours = emu:read16(profile.save2 + 0x0E)
    local minutes = emu:read8(profile.save2 + 0x10)
    local seconds = emu:read8(profile.save2 + 0x11)
    if count < 1 or count > 6 or currentBox > 13 or hours > 999 or minutes > 59 or seconds > 59 then
        error("No valid loaded save or reference layout mismatch. Check the memory profile.")
    end
    local pokemon = {}
    for slot = 0, count - 1 do
        table.insert(pokemon, string.format('{"hex":"%s","container":"Party","slot":%d}', hex(profile.party + slot * 100, 100), slot))
    end
    -- Every snapshot covers all 14 boxes. A temporary partial read must never look like a release.
    for box = 0, 13 do
        for slot = 0, 29 do
            local address = profile.storage + 4 + (box * 30 + slot) * 80
            local flags = emu:read8(address + 19)
            if (flags & 2) ~= 0 then
                table.insert(pokemon, string.format('{"hex":"%s","container":"Box","slot":%d,"box":%d}', hex(address, 80), slot, box))
            end
        end
    end
    local battle = (emu:read8(profile.main + 0x43D) & 2) ~= 0
    return string.format('{"version":1,"type":"snapshot","gameCode":"AXVD","layoutId":"%s","epoch":"%s","frame":%d,"playSeconds":%d,"mapGroup":%d,"mapNumber":%d,"area":255,"inBattle":%s,"completeInventory":true,"pokemon":[%s]}\n',
        profile.id, epoch, frame, hours * 3600 + minutes * 60 + seconds,
        emu:read8(profile.save1 + 4), emu:read8(profile.save1 + 5), tostring(battle), table.concat(pokemon, ","))
end

local function onFrame()
    tick = tick + 1
    local frame = emu:currentFrame()
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
