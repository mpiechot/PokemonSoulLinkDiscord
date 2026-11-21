-- Ruby observation bridge. The desktop player can opt into local PC item grants.
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
        stats = 0x0820BBE8, trainers = 0x081FD478,
        mapHeader = 0x0202E828, avatar = 0x0202E858, objects = 0x030048B0,
        battle = {flags = 0x020239F8, outcome = 0x02024D26, count = 0x02024A68, slots = 0x02024A6A, sides = 0x02024A72,
            mons = 0x02024A80, lastMoves = 0x02024C3C, lastHitBy = 0x02024C5C, trainer = 0x0202FF5E,
            active = 0x02024A60, move = 0x02024BE6, damage = 0x02024BEC, dealt = 0x02024BF0,
            attacker = 0x02024C07, target = 0x02024C08, script = 0x02024C10, hitMarker = 0x02024C6C}
    }
}
local port = SOULLINK_PORT or 5091
local itemSession = SOULLINK_ITEM_SESSION
SOULLINK_ITEM_SESSION = nil
local peer, pending, position = nil, nil, 1
local itemInput, itemReply, lastItemRequest = "", nil, nil
local function isPcGrantItem(item)
    return item and ((item >= 1 and item <= 12) or item == 68)
end
local epochSeed = tostring({}):gsub("[^%w]", "") .. tostring(os.time()) .. "_"
local epochSerial, epoch = 0, epochSeed .. "0"
local tick, lastFrame = 0, -1
local lastError = ""
local activeProfile
local battleFaints, lastPosition = {}, "null"
local faintDirty = false
local canStartWild, wildBattle, wildResults, wildDirty = false, nil, {}, false
local battleStarted, liveBattle, battleDirty = nil, "null", false
local nextHit
local gymLineups, rubyTypes

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
    itemInput, itemReply = "", nil
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

local function readGym(profile, gym)
    if not profile.trainers then return "null" end
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

local function readPosition(profile, group)
    if not profile.mapHeader or not profile.avatar or not profile.objects then return "null" end
    local layout = read32(profile.mapHeader)
    local avatar = read8(profile.avatar + 5)
    if layout < 0x08000000 or layout >= 0x0A000000 or avatar >= 16 then return "null" end
    local object = profile.objects + 36 * avatar
    if (read8(object) & 1) == 0 or (read8(object + 2) & 1) == 0 then return "null" end
    local x, y = read16(object + 0x10) - 7, read16(object + 0x12) - 7
    local width, height = read32(layout), read32(layout + 4)
    if width < 1 or width > 1024 or height < 1 or height > 1024 or x < 0 or x >= width or y < 0 or y >= height then return "null" end
    return string.format('{"x":%d,"y":%d,"width":%d,"height":%d,"outdoor":%s}', x, y, width, height, tostring(group == 0))
end

local function readTrainerId(profile)
    return read16(profile.save2 + 0x0A) | (read16(profile.save2 + 0x0C) << 16)
end

local function grantPcItem(profile, item)
    local count = read8(profile.partyCount)
    if (read8(profile.main + 0x43D) & 2) ~= 0 or count < 1 or count > 6 or
        read8(profile.avatar + 6) ~= 0 or readPosition(profile, read8(profile.save1 + 4)) == "null" then
        return "location"
    end

    -- Ruby's SaveBlock1 has 50 unencrypted {u16 id, u16 count} PC slots. Prepare the full grant before writing.
    local remaining, free, changes, total = 99, nil, {}, 0
    for slot = 0, 49 do
        local address = profile.save1 + 0x498 + 4 * slot
        local id, quantity = read16(address), read16(address + 2)
        if id > 348 or (id == 0 and quantity ~= 0) or (id ~= 0 and (quantity == 0 or quantity > 999)) then
            return "storage"
        end
        if id == 0 and not free then free = address end
        if id == item then total = total + quantity end
        if id == item and remaining > 0 and quantity < 999 then
            local added = math.min(remaining, 999 - quantity)
            changes[#changes + 1] = {address, item | ((quantity + added) << 16)}
            remaining = remaining - added
        end
    end
    if remaining > 0 then
        if not free then return "full" end
        changes[#changes + 1] = {free, item | (remaining << 16)}
    end
    for _, change in ipairs(changes) do readCore():write32(change[1], change[2]) end
    for _, change in ipairs(changes) do
        if read32(change[1]) ~= change[2] then return "changed" end
    end
    return "ok", total + 99
end

local function receiveLocalAction()
    if not itemSession or not peer or itemReply then return end
    if peer:hasdata() then
        local chunk = peer:receive(256)
        if chunk then itemInput = itemInput .. chunk end
    end
    if #itemInput > 256 then stop(); return end
    local ending = itemInput:find("\n", 1, true)
    if not ending then return end
    local line = itemInput:sub(1, ending - 1)
    itemInput = itemInput:sub(ending + 1)
    local action, token, expectedEpoch, trainer, request, kind, expires =
        line:match("^(%u+) (%x+) ([%w_]+) (%d+) ([%x%-]+) (%d+) (%d+)$")
    local item = tonumber(kind)
    local save = action == "SAVE" and item == 1
    if token ~= itemSession or not request or #request ~= 36 or request == lastItemRequest or
        not (save or (action == "PC" and isPcGrantItem(item))) then return end
    lastItemRequest = request
    local ok, outcome, quantity = pcall(function()
        local profile = activeProfile
        if profile ~= profiles.AXVD or epoch ~= expectedEpoch or readTrainerId(profile) ~= tonumber(trainer) or tonumber(expires) < os.time() then
            return "changed"
        end
        if save then return readCore():saveStateSlot(1) and "ok" or "save" end
        return grantPcItem(profile, item)
    end)
    itemReply = string.format('{"version":1,"type":"pc-item-result","itemSession":"%s","itemRequest":"%s","itemResult":"%s","itemQuantity":%s}\n',
        itemSession, request, ok and outcome or "changed", quantity or "null")
end

local function pendingDirectHit(a, count, frame)
    if not a.script then return nil end
    local script = read32(a.script)
    -- atk0C_datahpupdate with BS_TARGET and no passive HP update.
    if script < 0x08000000 or script >= 0x09FFFFFF or read8(script) ~= 0x0C or read8(script + 1) ~= 0 or
        (read32(a.hitMarker) & 0x100000) ~= 0 then return nil end
    local attacker, target, move = read8(a.attacker), read8(a.target), read16(a.move)
    if attacker >= count or target >= count or move < 1 or move > 354 or
        (read8(a.sides + attacker) & 1) == 0 or (read8(a.sides + target) & 1) ~= 0 then return nil end
    local mon = a.mons + target * 88
    local hp, damage = read16(mon + 0x28), read32(a.damage)
    local species = read16(a.mons + attacker * 88)
    if hp == 0 or damage < hp or damage > 0x7FFFFFFF or species == 0 then return nil end
    return {frame = frame, attacker = attacker, target = target, move = move, species = species,
        hp = hp, pid = read32(mon + 0x48), ot = read32(mon + 0x54)}
end

local function battleTrainer(profile)
    if profile.trainers and (read32(profile.battle.flags) & 8) ~= 0 then
        local id = read16(profile.battle.trainer)
        if id > 0 and id < 694 then return '"' .. hex(profile.trainers + id * 40 + 4, 12) .. '"' end
    end
    return "null"
end

local function captureBattle(profile, frame)
    local addresses = profile.battle
    local prior = nextHit
    nextHit = nil
    if not addresses or (read8(profile.main + 0x43D) & 2) == 0 then return end
    local count = read8(addresses.count)
    if count < 2 or count > 4 then return end
    nextHit = pendingDirectHit(addresses, count, frame)
    local trainer = battleTrainer(profile)
    for battler = 0, count - 1 do
        local partySlot = read16(addresses.slots + battler * 2)
        if (read8(addresses.sides + battler) & 1) == 0 and partySlot < 6 then
            local mon = addresses.mons + battler * 88
            local hp = read16(mon + 0x28)
            if hp == 0 and read16(mon + 0x2C) > 0 then
                local attacker = read8(addresses.lastHitBy + battler)
                local move = read16(addresses.lastMoves + battler * 2)
                local species = 0
                if attacker < count and (read8(addresses.sides + attacker) & 1) ~= 0 then
                    species = read16(addresses.mons + attacker * 88)
                end
                local pid, ot = read32(mon + 0x48), read32(mon + 0x54)
                local finishing = prior and prior.frame + 1 == frame and prior.target == battler and prior.pid == pid and prior.ot == ot and
                    read8(addresses.active) == battler and read8(addresses.attacker) == prior.attacker and
                    read16(addresses.move) == prior.move and read32(addresses.dealt) == prior.hp
                if finishing then species, move = prior.species, prior.move end
                local earlier = battleFaints[partySlot]
                if not earlier or earlier.pid ~= pid or earlier.ot ~= ot or (finishing and not earlier.finishing) or
                    (earlier.move == 0 and species > 0 and move > 0 and move < 355) then
                    battleFaints[partySlot] = {species = species, move = move > 0 and move < 355 and species > 0 and move or 0,
                        trainer = trainer, pid = pid, ot = ot, finishing = finishing == true, frame = frame}
                    faintDirty = true
                end
            elseif hp > 0 then
                battleFaints[partySlot] = nil
            end
        end
    end
end

local function observeLiveBattle(profile, frame)
    if profile ~= profiles.AXVD then return end
    local addresses = profile.battle
    local scene = "null"
    if (read8(profile.main + 0x43D) & 2) ~= 0 then
        if not battleStarted and read8(addresses.outcome) ~= 0 then return end
        local count = read8(addresses.count)
        battleStarted = battleStarted or (wildBattle and wildBattle.started) or frame
        if count == 2 or count == 4 then
            local combatants, seen = {}, {}
            for index = 0, count - 1 do
                local position = read8(addresses.sides + index)
                local mon = addresses.mons + index * 88
                local species, hp, maxHp = read16(mon), read16(mon + 0x28), read16(mon + 0x2C)
                if position < 4 and not seen[position] and species > 0 and species < 412 and maxHp > 0 and maxHp <= 999 and hp <= maxHp then
                    seen[position] = true
                    if wildBattle and (position & 1) ~= 0 and not wildBattle.species then wildBattle.species = species end
                    combatants[#combatants + 1] = string.format('{"position":%d,"species":%d,"hp":%d,"maxHp":%d,"personality":%d}',
                        position, species, hp, maxHp, read32(mon + 0x48))
                end
            end
            if #combatants > 0 then
                scene = string.format('{"started":%d,"trainerHex":%s,"catchEncounter":%s,"combatants":[%s]}',
                    battleStarted, battleTrainer(profile), tostring(wildBattle ~= nil), table.concat(combatants, ","))
            end
        end
    else
        battleStarted = nil
    end
    if liveBattle ~= scene then liveBattle, battleDirty = scene, true end
end

local function hasBagBalls(profile)
    -- Ruby's 16 bag slots hold unencrypted {u16 id, u16 count} pairs.
    for slot = 0, 15 do
        local entry = read32(profile.save1 + 0x600 + 4 * slot)
        local id, quantity = entry & 0xFFFF, entry >> 16
        if id >= 1 and id <= 12 and quantity > 0 and quantity <= 999 then return true end
    end
    return false
end

local function observeWildBattle(profile, frame)
    if profile ~= profiles.AXVD then return end
    local outcome = read8(profile.battle.outcome)
    if (read8(profile.main + 0x43D) & 2) == 0 then
        if wildBattle and wildBattle.record and frame > wildBattle.started and outcome >= 1 and outcome <= 10 then
            wildResults[#wildResults + 1] = string.format('{"mapGroup":%d,"mapNumber":%d,"started":%d,"ended":%d,"outcome":%d,"species":%s}',
                wildBattle.group, wildBattle.number, wildBattle.started, frame, outcome, wildBattle.species or "null")
            if #wildResults > 16 then table.remove(wildResults, 1) end
            wildDirty = true
        end
        wildBattle = nil
        canStartWild = canStartWild or readPosition(profile, read8(profile.save1 + 4)) ~= "null"
        return
    end
    if battleStarted or wildBattle or outcome ~= 0 then return end

    local flags = read32(profile.battle.flags)
    -- Normal and roaming wild battles only. Starter and tutorial battles have their own flags.
    if (flags & ~0x404) == 0 and hasBagBalls(profile) then
        wildBattle = {group = read8(profile.save1 + 4), number = read8(profile.save1 + 5), started = frame, record = canStartWild}
    end
    canStartWild = false
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

local function readBattlePartyHealth(profile, partyCount)
    local health = {}
    local addresses = profile.battle
    if not addresses then return health end
    local count = read8(addresses.count)
    if count < 2 or count > 4 then return health end
    for battler = 0, count - 1 do
        local slot = read16(addresses.slots + battler * 2)
        if (read8(addresses.sides + battler) & 1) == 0 and slot < partyCount then
            local mon = addresses.mons + battler * 88
            local party = profile.party + slot * 100
            local hp, max = read16(mon + 0x28), read16(mon + 0x2C)
            if max > 0 and max == read16(party + 88) and hp <= max and
                read32(mon + 0x48) == read32(party) and read32(mon + 0x54) == read32(party + 4) then
                health[slot] = hp
            end
        end
    end
    return health
end

local function readProfile()
    -- Header identity tolerates randomization; structural checks reject invalid memory reads.
    local code = readRange(0x080000AC, 4)
    local title = readRange(0x080000A0, 12):gsub("%z", "")
    local reference = profiles[code]
    local revision = read8(0x080000BC)
    if not reference or title:gsub("%s+$", "") ~= reference.title or not reference.revisions[revision] then
        error(string.format("No read-only profile for ROM %q revision %d.", code, revision), 0)
    end
    return code, SOULLINK_PROFILE or reference, reference
end

local function snapshot(frame)
    local code, profile, reference = readProfile()
    activeProfile = profile
    local count = read8(profile.partyCount)
    local currentBox = read8(profile.storage)
    local hours = read16(profile.save2 + 0x0E)
    local minutes = read8(profile.save2 + 0x10)
    local seconds = read8(profile.save2 + 0x11)
    local trainerId = readTrainerId(profile)
    local money = profile == reference and read32(profile.save1 + 0x490) or -1
    if count > 6 or currentBox > 13 or hours > 999 or minutes > 59 or seconds > 59 then
        error(string.format("Invalid %s memory layout: party=%d, box=%d, play time=%d:%02d:%02d.",
            code, count, currentBox, hours, minutes, seconds), 0)
    end
    local battle = (read8(profile.main + 0x43D) & 2) ~= 0
    local group, number = read8(profile.save1 + 4), read8(profile.save1 + 5)
    if not battle then
        local positionOk, currentPosition = pcall(readPosition, profile, group)
        lastPosition = positionOk and currentPosition or "null"
    end
    if count == 0 and (battle or lastPosition == "null") then
        error("Waiting for a loaded save or a new game on the field.", 0)
    end
    local pokemon = {}
    local battleHealth = battle and readBattlePartyHealth(profile, count) or {}
    for slot = 0, count - 1 do
        table.insert(pokemon, string.format('{"hex":"%s","container":"Party","slot":%d,"liveHp":%s}',
            hex(profile.party + slot * 100, 100), slot, battleHealth[slot] or "null"))
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
    local faints = {}
    for slot = 0, count - 1 do
        local hp = read16(profile.party + slot * 100 + 86)
        if hp > 0 and not battle then battleFaints[slot] = nil end
        local faint = battleFaints[slot]
        if faint and (hp == 0 or (battle and frame - faint.frame < 120)) and
            read32(profile.party + slot * 100) == faint.pid and read32(profile.party + slot * 100 + 4) == faint.ot then
            faints[#faints + 1] = string.format('{"partySlot":%d,"opponentSpecies":%s,"move":%s,"trainerHex":%s,"finishingBlow":%s}',
                slot, faint.species > 0 and faint.species or "null", faint.move > 0 and faint.move or "null", faint.trainer, tostring(faint.finishing))
        end
    end
    -- Badges are eight consecutive flags starting at FLAG_BADGE01_GET (0x807).
    local systemFlags = read16(profile.save1 + 0x1220 + 0x100)
    local badges = (systemFlags >> 7) & 0xFF
    -- The Route 101 starter script sets Pokémon Get before the lab awards the Pokédex.
    local starterWindow = profile == reference and (systemFlags & 3) == 1
    if not gymLineups then
        gymLineups = {}
        for index = 0, 7 do
            local ok, lineup = pcall(readGym, profile, index)
            gymLineups[index + 1] = ok and lineup or "null"
        end
    end
    local gym = "null"
    for index = 0, 7 do
        if (badges & (1 << index)) == 0 then gym = gymLineups[index + 1]; break end
    end
    if not rubyTypes then
        local typesOk, types = pcall(readTypes, profile)
        rubyTypes = typesOk and types or ""
    end
    local itemCapability = itemSession and profile == reference and '"' .. itemSession .. '"' or "null"
    return string.format('{"version":1,"type":"snapshot","gameCode":"%s","layoutId":"%s","epoch":"%s","frame":%d,"playSeconds":%d,"trainerId":%d,"money":%s,"itemSession":%s,"mapGroup":%d,"mapNumber":%d,"area":255,"position":%s,"inBattle":%s,"battle":%s,"faints":[%s],"wildBattles":[%s],"badgeMask":%d,"rubyStarterWindow":%s,"gymLevels":%s,"gymLineups":[%s],"rubyTypesHex":"%s","completeInventory":true,"pokemon":[%s]}\n',
        code, profile.id, epoch, frame, hours * 3600 + minutes * 60 + seconds, trainerId,
        money >= 0 and money <= 999999 and tostring(money) or "null", itemCapability,
        group, number, lastPosition, tostring(battle), liveBattle, table.concat(faints, ","), table.concat(wildResults, ","), badges,
        tostring(starterWindow), gym, table.concat(gymLineups, ","), rubyTypes, table.concat(pokemon, ","))
end

local function reset()
    epochSerial = epochSerial + 1
    epoch = epochSeed .. tostring(epochSerial)
    activeProfile, battleFaints, lastPosition, gymLineups, rubyTypes = nil, {}, "null", nil, nil
    faintDirty, nextHit, lastFrame = false, nil, -1
    canStartWild, wildBattle, wildResults, wildDirty = false, nil, {}, false
    battleStarted, liveBattle, battleDirty = nil, "null", false
    stop()
end

local function onFrame()
    tick = tick + 1
    local frame = readCore():currentFrame()
    if lastFrame >= 0 and frame < lastFrame then reset() end
    lastFrame = frame
    if activeProfile then
        pcall(captureBattle, activeProfile, frame)
        pcall(observeWildBattle, activeProfile, frame)
        if tick % 5 == 0 or battleStarted == nil or (read8(activeProfile.main + 0x43D) & 2) == 0 then
            pcall(observeLiveBattle, activeProfile, frame)
        end
    end
    sendPending()
    receiveLocalAction()
    if itemReply and not pending then
        pending, position, itemReply = itemReply, 1, nil
        sendPending()
    end
    if (tick % 30 ~= 0 and not faintDirty and not wildDirty and not battleDirty) or pending then return end
    if not peer then
        local valid, profileOrError = pcall(readProfile)
        if not valid then report(tostring(profileOrError)); return end
        peer = socket.tcp()
        peer:add("error", function() stop() end)
        if not peer:connect("127.0.0.1", port) then stop(); report("Local client unavailable; retrying."); return end
        if itemSession then
            pending = string.format('{"version":1,"type":"bridge-ready","itemSession":"%s"}\n', itemSession)
            sendPending()
            if not peer or pending then return end
        end
    end
    local ok, result = pcall(snapshot, frame)
    if not ok then report(tostring(result)); return end
    pending, position = result, 1
    faintDirty, wildDirty, battleDirty = false, false, false
    sendPending()
    report("Streaming read-only reference profile. Check party and box contents in the client before starting the run.")
end

-- Re-loading this file removes only callbacks installed by its previous instance.
if SOULLINK_STOP then SOULLINK_STOP() end
local frameCallback = callbacks:add("frame", onFrame)
local resetCallback = callbacks:add("reset", reset)
local loadCallback = callbacks:add("loadState", reset)
SOULLINK_STOP = function()
    callbacks:remove(frameCallback)
    callbacks:remove(resetCallback)
    callbacks:remove(loadCallback)
    stop()
end
console:log(itemSession and "SoulLink bridge with local PC item menu loaded." or "SoulLink read-only bridge loaded.")
