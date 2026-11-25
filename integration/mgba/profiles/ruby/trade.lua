-- AXVD revisions 0 and 1. Use Rubin's local NPC trade scene with a prepared incoming Pokémon.
return function(readCore, profile, checkpointPrefix, hideAvatars)
    local enemy, chooseNpc, chooseSlot, tradeScene = 0x030045D0, 0x0202E8CC, 0x0202E8CE, 0x0804BAF5
    local expTable, baseStats = 0x0820AF48, 0x0820BBE8
    local orders = {"GAEM","GAME","GEAM","GEMA","GMAE","GMEA","AGEM","AGME","AEGM","AEMG","AMGE","AMEG",
        "EGAM","EGMA","EAGM","EAMG","EMGA","EMAG","MGAE","MGEA","MAGE","MAEG","MEGA","MEAG"}
    local holdUntil, active = 0, nil

    local function read(address, size) return readCore():readRange(address, size) end

    local function write(address, data)
        for i = 1, #data do readCore():write8(address + i - 1, data:byte(i)) end
    end

    local function toHex(data)
        return (data:gsub(".", function(byte) return string.format("%02X", byte:byte()) end))
    end

    local function fromHex(data)
        if #data ~= 160 and #data ~= 200 then return end
        return (data:gsub("%x%x", function(pair) return string.char(tonumber(pair, 16)) end))
    end

    local function identity(data)
        local pid, ot = string.unpack("<I4I4", data)
        return string.format("%08X%08X", ot, pid)
    end

    local function parse(data)
        if not data or (#data ~= 80 and #data ~= 100) or data:byte(20) & 7 ~= 2 then return end
        local pid, ot = string.unpack("<I4I4", data)
        local order, parts, checksum = orders[pid % 24 + 1], {}, 0
        for index = 0, 3 do
            local a, b, c = string.unpack("<I4I4I4", data, 33 + index * 12)
            local plain = string.pack("<I4I4I4", a ~ pid ~ ot, b ~ pid ~ ot, c ~ pid ~ ot)
            parts[order:sub(index + 1, index + 1)] = plain
            for offset = 1, 11, 2 do checksum = (checksum + string.unpack("<I2", plain, offset)) & 0xFFFF end
        end
        if checksum ~= string.unpack("<I2", data, 29) then return end
        local species, item, experience = string.unpack("<I2I2I4", parts.G)
        local ivs = string.unpack("<I4", parts.M, 5)
        if species < 1 or species > 411 or (species >= 252 and species <= 276) or experience > 1640000 or
            (ivs & 0x40000000) ~= 0 or item > 348 or (item >= 121 and item <= 132) then return end
        if #data == 100 and (data:byte(86) ~= 255 or data:byte(85) < 1 or data:byte(85) > 100 or
            string.unpack("<I2", data, 87) == 0) then return end
        for offset = 1, 7, 2 do if string.unpack("<I2", parts.A, offset) > 354 then return end end
        for _, name in ipairs({data:sub(9, 18), data:sub(21, 27)}) do
            for byte in name:gmatch(".") do
                if byte:byte() == 255 then break end
                if byte:byte() >= 250 then return end
            end
        end
        return { species = species, experience = experience, evs = parts.E, ivs = ivs, nature = pid % 25 }
    end

    local function asParty(data, mon)
        if #data == 100 then return data end
        local stats = read(baseStats + mon.species * 28, 28)
        local growth = stats:byte(20)
        if growth > 5 then return end
        local level = 1
        while level < 100 and readCore():read32(expTable + (growth * 101 + level + 1) * 4) <= mon.experience do
            level = level + 1
        end
        local values = {}
        local plus, minus = mon.nature // 5, mon.nature % 5
        for i = 0, 5 do
            local iv = (mon.ivs >> (i * 5)) & 31
            local value = ((2 * stats:byte(i + 1) + iv + mon.evs:byte(i + 1) // 4) * level) // 100
            if i == 0 then
                value = mon.species == 303 and 1 or value + level + 10 -- Shedinja in Ruby's internal numbering.
            else
                value = value + 5
                if plus ~= minus and i - 1 == plus then value = value * 110 // 100 end
                if plus ~= minus and i - 1 == minus then value = value * 90 // 100 end
            end
            values[i + 1] = value
        end
        return data .. string.pack("<I4BBI2I2I2I2I2I2I2", 0, level, 255, values[1], table.unpack(values))
    end

    local function field()
        local core = readCore()
        return profile.fieldCallbacks[core:read32(profile.main + 4) & ~1] and
            (readCore():read8(profile.main + 0x43D) & 2) == 0 and
            readCore():read8(profile.avatar + 5) < 16 and readCore():read8(profile.avatar + 6) == 0 and
            readCore():read16(tradeScene & ~1) == 0xB570
    end

    local function lookup(outgoing, incoming)
        local first, second, count
        local function inspect(address, slot, box)
            local prefix = read(address, 20)
            if prefix:byte(20) & 2 == 0 then return end
            local key = identity(prefix)
            if key ~= outgoing and key ~= incoming then return end
            local match = { address = address, slot = slot, box = box, data = read(address, box and 80 or 100) }
            if key ~= incoming then first = match
            else second = match end
            count = count + 1
        end
        count = 0
        local partyCount = readCore():read8(profile.partyCount)
        if partyCount < 1 or partyCount > 6 then return nil, nil, 0 end
        for slot = 0, partyCount - 1 do inspect(profile.party + slot * 100, slot) end
        for box = 0, 13 do
            for slot = 0, 29 do inspect(profile.storage + 4 + (box * 30 + slot) * 80, slot, box) end
        end
        return first, second, count
    end

    local function checkpoint(id, before)
        local path = checkpointPrefix .. id .. (before and "-before.ss" or "-after.ss")
        local file = io.open(path, "rb")
        if file then
            local size = file:seek("end")
            file:close()
            if size and size > 0 then return true end
            os.remove(path)
        end
        hideAvatars()
        local temporary = path .. ".tmp"
        if not readCore():saveStateFile(temporary) then return false end
        return os.rename(temporary, path) == true
    end

    local function act(id, action, trainer, outgoing, preparedHex, incomingHex)
        if active then return "busy" end
        if not field() then return "field" end
        if string.unpack("<I4", read(profile.save2 + 0x0A, 4)) ~= trainer then return "conflict" end
        local incoming = action == "APPLY" and fromHex(incomingHex) or nil
        local incomingMon = parse(incoming)
        if action == "APPLY" and not incomingMon then return "ineligible" end
        local own, received, count = lookup(outgoing, incoming and identity(incoming))
        if action == "PREP" then
            if count ~= 1 or not own or not parse(own.data) then return "ineligible" end
            if not checkpoint(id, true) then return "storage" end
            holdUntil = os.time() + 3
            return "ready", own
        end
        if count == 1 and received and not own and parse(received.data) then
            return checkpoint(id, false) and "applied" or "storage", received
        end
        if count ~= 1 or not own or toHex(own.data) ~= preparedHex:upper() then return "conflict" end
        local ownMon = parse(own.data)
        local inParty = asParty(incoming, incomingMon)
        local outParty = ownMon and asParty(own.data, ownMon)
        if not inParty or not outParty then return "ineligible" end
        hideAvatars()
        local slot = own.box and 0 or own.slot
        local savedParty = own.box and read(profile.party, 100) or nil
        if savedParty then write(profile.party, outParty) end
        write(enemy, inParty)
        readCore():write16(chooseNpc, 0)
        readCore():write16(chooseSlot, slot)
        readCore():write8(profile.main + 0x43C, 0)
        readCore():write32(profile.main + 4, tradeScene)
        active = { id = id, own = own, expected = identity(incoming), slot = slot, savedParty = savedParty }
        return "busy"
    end

    return {
        act = act,
        running = function() return active ~= nil end,
        frame = function(tick)
            if not active or not field() then return end
            local data = read(profile.party + active.slot * 100, 100)
            if identity(data) ~= active.expected or not parse(data) then return end
            if active.savedParty then
                write(active.own.address, data:sub(1, 80))
                write(profile.party, active.savedParty)
            end
            local test = active.own.box and data:sub(1, 80) or data
            if read(active.own.address, #test) ~= test then return end
            checkpoint(active.id, false)
            holdUntil = os.time() + 1
            active = nil
        end,
        keys = function(tick)
            if active or os.time() < holdUntil then readCore():setKeys(active and tick % 12 == 0 and 1 or 0) end
        end,
        locked = function() return active ~= nil or os.time() < holdUntil end,
        reset = function() active, holdUntil = nil, 0 end,
    }
end
