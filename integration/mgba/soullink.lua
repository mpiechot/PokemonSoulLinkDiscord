-- SoulLink mGBA bridge. The Windows player exports a self-contained copy before launching mGBA.
local games = SOULLINK_GAMES or require("games")
local port = SOULLINK_PORT or 5091
local itemSession = SOULLINK_ITEM_SESSION
SOULLINK_ITEM_SESSION = nil
local peer, pending, position = nil, nil, 1
local itemInput, itemReply, lastItemRequest = "", nil, nil
local loadingSlot, resetAfterReply = false, false
local epochSeed = tostring({}):gsub("[^%w]", "") .. tostring(os.time()) .. "_"
local epochSerial, epoch = 0, epochSeed .. "0"
local tick, lastFrame = 0, -1
local lastError = ""
local activeGame
local overworldError

-- Resolve the core for each read because the newer bindings return a short-lived reference.
local function readCore()
    local ok, core = pcall(function() return emu._core end)
    if ok and core then return core end
    return emu
end

local function stop()
    if peer then pcall(function() peer:close() end) end
    peer, pending, position = nil, nil, 1
    itemInput, itemReply = "", nil
    if activeGame and activeGame.overworld then pcall(activeGame.overworld.reset) end
end

local function report(message)
    if message ~= lastError then console:log("SoulLink: " .. message); lastError = message end
end

local function saveSlot(slot)
    local overworld = activeGame and activeGame.overworld
    if overworld then pcall(overworld.hide) end
    return readCore():saveStateSlot(slot)
end

local function selectGame()
    local extensions = {
        saveSlot = saveSlot,
        report = report,
        features = itemSession and SOULLINK_FEATURES or {},
        checkpoint = SOULLINK_TRADE_CHECKPOINT,
        memoryLayout = SOULLINK_PROFILE,
    }
    local diagnostic
    for _, identify in ipairs(games) do
        local ok, result = pcall(identify, readCore, extensions)
        if ok and result then return result end
        if not ok then diagnostic = result end
    end
    error(diagnostic or "No SoulLink profile for the loaded cartridge.", 0)
end

local function handleLocalAction(line)
    if #line > 256 then return end
    local action, token, expectedEpoch, trainer, request, kind, expires =
        line:match("^(%u+) (%x+) ([%w_]+) (%d+) ([%x%-]+) (%d+) (%d+)$")
    local item = tonumber(kind)
    local save = action == "SAVE" and item == 1
    local load = action == "LOAD" and item == 1
    if token ~= itemSession or not request or #request ~= 36 or request == lastItemRequest or
        not (save or load or (action == "PC" or action == "BAG")) then return end
    lastItemRequest = request
    local ok, outcome, quantity = pcall(function()
        local game = activeGame
        if not game or not game.localActions or (game.trade and game.trade.locked()) or
            epoch ~= expectedEpoch or tonumber(expires) < os.time() then
            return "changed"
        end
        if load then
            loadingSlot = true
            local loaded = readCore():loadStateSlot(1)
            resetAfterReply = resetAfterReply or loaded
            return loaded and "ok" or "load"
        end
        if game.trainerId() ~= tonumber(trainer) then return "changed" end
        if save then return saveSlot(1) and "ok" or "save" end
        if not game.grantItem then return "changed" end
        return game.grantItem(item, action == "BAG")
    end)
    loadingSlot = false
    itemReply = string.format('{"version":1,"type":"pc-item-result","itemSession":"%s","itemRequest":"%s","itemResult":"%s","itemQuantity":%s}\n',
        itemSession, request, ok and outcome or "changed", quantity or "null")
end

local function handleTradeAction(line)
    if #line > 640 then return end
    local token, expectedEpoch, trainer, request, id, action, identity, prepared, incoming =
        line:match("^TRADE (%x+) ([%w_]+) (%d+) ([%x%-]+) (%x+) (%u+) (%x+) ([%x%-]+) ([%x%-]+)$")
    if token ~= itemSession or not request or #request ~= 36 or #id ~= 32 or #identity ~= 16 or
        request == lastItemRequest or (action ~= "PREP" and action ~= "APPLY") then return end
    lastItemRequest = request
    local ok, status, pokemon = pcall(function()
        local trade = activeGame and activeGame.trade
        if not trade or expectedEpoch ~= epoch then return "field" end
        return trade.act(id, action, tonumber(trainer), identity, prepared, incoming)
    end)
    local data = "null"
    if ok and pokemon then
        data = string.format('{"hex":"%s","container":"%s","slot":%d,"box":%s}',
            (pokemon.data:gsub(".", function(byte) return string.format("%02X", byte:byte()) end)), pokemon.box and "Box" or "Party", pokemon.slot, pokemon.box or "null")
    end
    itemReply = string.format('{"version":1,"type":"pc-item-result","itemSession":"%s","itemRequest":"%s","itemResult":"%s","tradePokemon":%s}\n',
        itemSession, request, ok and status or "storage", data)
end

local function receiveLocalAction()
    if not itemSession or not peer or itemReply then return end
    if peer:hasdata() then
        local chunk = peer:receive(4096)
        if chunk then itemInput = itemInput .. chunk end
    end
    if #itemInput > 8192 then stop(); return end
    local overworld = activeGame and activeGame.overworld
    for _ = 1, 8 do
        local ending = itemInput:find("\n", 1, true)
        if not ending or itemReply then return end
        local line = itemInput:sub(1, ending - 1)
        itemInput = itemInput:sub(ending + 1)
        if line:sub(1, 8) == "AVATARS " then
            local token, expectedEpoch, poses = line:match("^AVATARS (%x+) ([%w_]+) (.*)$")
            if overworld and token == itemSession and expectedEpoch == epoch and #line <= 2048 then
                overworld.update(poses, tick)
            end
        elseif line:sub(1, 12) == "AVATARSTYLE " then
            local token, expectedEpoch, tint, labels = line:match("^AVATARSTYLE (%x+) ([%w_]+) ([%x%-]+) ([01])$")
            if overworld and token == itemSession and expectedEpoch == epoch and
                (tint == "-" or tint:match("^%x%x%x%x%x%x$")) then
                overworld.style(tonumber(tint, 16), labels == "1")
            end
        elseif line:sub(1, 6) == "TRADE " then
            handleTradeAction(line)
        else
            handleLocalAction(line)
        end
    end
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

local function reset()
    resetAfterReply = false
    epochSerial = epochSerial + 1
    epoch = epochSeed .. tostring(epochSerial)
    lastFrame = -1
    if activeGame and activeGame.trade then activeGame.trade.reset() end
    stop()
    activeGame = nil
end

local function flushActionReply()
    if peer and itemReply and not pending then
        pending, position, itemReply = itemReply, 1, nil
        sendPending()
    end
end

local function onFrame()
    if resetAfterReply then
        sendPending()
        flushActionReply()
        if not peer or not pending then reset() end
        return
    end
    tick = tick + 1
    local frame = readCore():currentFrame()
    if (lastFrame >= 0 and frame < lastFrame) or (activeGame and not activeGame.currentCartridge()) then reset() end
    lastFrame = frame
    if activeGame then activeGame.observe(frame, tick) end
    sendPending()
    receiveLocalAction()
    flushActionReply()
    local trade = activeGame and activeGame.trade
    if trade and trade.running() then
        trade.frame(tick)
        if trade.running() then return end
    end
    if resetAfterReply then
        if not peer or not pending then reset() end
        return
    end
    if itemSession and activeGame and activeGame.autosave then activeGame.autosave() end
    local overworld = activeGame and activeGame.overworld
    if overworld and peer then
        local ok, pose = pcall(overworld.step, tick, tick % 4 == 0 and not pending)
        if ok and pose then
            pending, position = string.format('{"version":1,"type":"overworld","itemSession":"%s","epoch":"%s","overworld":%s}\n',
                itemSession, epoch, pose), 1
            sendPending()
        elseif not ok and pose ~= overworldError then
            overworldError = pose
            console:log("SoulLink overworld: " .. tostring(pose))
            pcall(overworld.reset)
        end
    end
    if (tick % 30 ~= 0 and not (activeGame and activeGame.dirty())) or pending then return end
    if not peer then
        if not activeGame then
            local valid, game = pcall(selectGame)
            if not valid then report(tostring(game)); return end
            activeGame = game
        end
        peer = socket.tcp()
        peer:add("error", function() stop() end)
        if not peer:connect("127.0.0.1", port) then stop(); report("Local client unavailable; retrying."); return end
        if itemSession and activeGame.localActions then
            pending = string.format('{"version":1,"type":"bridge-ready","itemSession":"%s","epoch":"%s"}\n',
                itemSession, epoch)
            sendPending()
            if not peer or pending then return end
        end
    end
    local ok, result = pcall(activeGame.snapshot, frame, epoch, itemSession)
    if not ok then report(tostring(result)); return end
    pending, position = result, 1
    activeGame.acknowledged()
    sendPending()
    report("Streaming a supported SoulLink game profile. Check party and box contents in the client before starting the run.")
end

-- Re-loading this file removes only callbacks installed by its previous instance.
if SOULLINK_STOP then SOULLINK_STOP() end
local frameCallback = callbacks:add("frame", onFrame)
local tradeKeys = SOULLINK_FEATURES and callbacks:add("keysRead", function()
    local trade = activeGame and activeGame.trade
    if trade and trade.locked() and activeGame.currentCartridge() then trade.keys(tick) end
end)
local resetCallback = callbacks:add("reset", reset)
local stopCallback = callbacks:add("stop", stop)
local loadCallback = callbacks:add("loadState", function()
    -- The client must receive the slot result before resetting the stream and taking a new baseline.
    if loadingSlot then resetAfterReply = true else reset() end
end)
SOULLINK_STOP = function()
    callbacks:remove(frameCallback)
    if tradeKeys then callbacks:remove(tradeKeys) end
    callbacks:remove(resetCallback)
    callbacks:remove(stopCallback)
    callbacks:remove(loadCallback)
    stop()
end
console:log(itemSession and "SoulLink bridge with local PC item menu loaded." or "SoulLink read-only bridge loaded.")
