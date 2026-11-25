-- German Ruby AXVD revisions 0 and 1. Addresses are from the pokeruby German symbol tables.
-- These are native drawing sprites only. Ruby's field objects and script state are never changed.
return function(readCore, profile)
    -- The bundled mGBA invalidates the Lua Core reference after each call.
    local core = {
        read8 = function(_, address) return readCore():read8(address) end,
        read16 = function(_, address) return readCore():read16(address) end,
        read32 = function(_, address) return readCore():read32(address) end,
        write8 = function(_, address, value) readCore():write8(address, value) end,
        write16 = function(_, address, value) readCore():write16(address, value) end,
        write32 = function(_, address, value) readCore():write32(address, value) end,
    }
    local sprites, spriteSize, dummy = 0x02020004, 0x44, 0x081EF830
    local dummyCallback = 0x08001345
    local tileBitmap, reservedTiles, objTiles = 0x03002450, 0x030024D8, 0x06010000
    local paletteTags, reservedPalettes = 0x03000300, 0x030028F8
    local unfaded, faded, objectPalettes = 0x0202ECC8, 0x0202F0C8, 0x0837EF64
    local camera, totalX, totalY = 0x03004890, 0x030048AC, 0x030048A8
    local coordOffsetX, coordOffsetY = 0x030024D0, 0x030027E0
    local graphicsTable, surfTemplates = 0x08379440, 0x083797A8
    local playerImages = {[0] = 18, [1] = 9, [2] = 12, [3] = 5, [63] = 27,
        [89] = 18, [90] = 9, [91] = 27, [92] = 12, [93] = 5, [111] = 9, [112] = 9,
        [137] = 12, [138] = 12, [191] = 9, [192] = 9, [193] = 1, [194] = 1}
    local depth = {0x73, 0x73, 0x53, 0x73, 0x53, 0x73, 0x53, 0x73, 0x53, 0x73, 0x53, 0x73, 0x53, 0, 0, 0x73}
    local marker, markerTail, paletteMarker, tileCount = 0x534C, 0x4156, 0x5340, 16
    local parts, tracks, paletteKeys = {}, {}, {}
    local lastPacket, lastPacketAt, cleared = nil, nil, false
    local localColor, localTint, showLabels = nil, nil, true
    local glyphs = {
        A={2,5,7,5,5}, B={6,5,6,5,6}, C={3,4,4,4,3}, D={6,5,5,5,6}, E={7,4,6,4,7}, F={7,4,6,4,4},
        G={3,4,5,5,3}, H={5,5,7,5,5}, I={7,2,2,2,7}, J={1,1,1,5,2}, K={5,5,6,5,5}, L={4,4,4,4,7},
        M={5,7,7,5,5}, N={5,7,7,7,5}, O={2,5,5,5,2}, P={6,5,6,4,4}, Q={2,5,5,7,3}, R={6,5,6,5,5},
        S={3,4,2,1,6}, T={7,2,2,2,2}, U={5,5,5,5,7}, V={5,5,5,5,2}, W={5,5,7,7,5}, X={5,5,2,5,5},
        Y={5,5,2,2,2}, Z={7,1,2,4,7}, ["0"]={7,5,5,5,7}, ["1"]={2,6,2,2,7}, ["2"]={6,1,2,4,7},
        ["3"]={6,1,2,1,6}, ["4"]={5,5,7,1,1}, ["5"]={7,4,6,1,6}, ["6"]={3,4,6,5,2}, ["7"]={7,1,2,2,2},
        ["8"]={2,5,2,5,2}, ["9"]={2,5,3,1,6}, [" "]={0,0,0,0,0}, ["-"]={0,0,7,0,0},
        ["_"]={0,0,0,0,7}, ["."]={0,0,0,0,2}, ["?"]={6,1,2,0,2},
        ["\196"]={5,2,5,7,5}, ["\214"]={5,2,5,5,2}, ["\220"]={5,0,5,5,7}, ["\223"]={2,5,6,5,6},
    }

    local function rom(address, size)
        return address >= 0x08000000 and address + size <= 0x0A000000 and address % 4 == 0
    end

    local function signed16(value)
        return ((value + 0x8000) & 0xFFFF) - 0x8000
    end

    local function copyWords(core, source, destination, size)
        for offset = 0, size - 4, 4 do core:write32(destination + offset, core:read32(source + offset)) end
    end

    local function isRuby(core)
        return core:read32(0x080000AC) == 0x44565841 and core:read8(0x080000BC) <= 1 and
            core:read32(dummy + 0x1C) == dummyCallback
    end

    local function readGraphics(core, graphics)
        if not playerImages[graphics] then return end
        local info = core:read32(graphicsTable + graphics * 4)
        if not rom(info, 36) then return end
        local width, height = core:read16(info + 8), core:read16(info + 10)
        local oam, subsprites = core:read32(info + 0x10), core:read32(info + 0x14)
        local images = core:read32(info + 0x1C)
        if (width ~= 16 and width ~= 32) or (height ~= 16 and height ~= 32) or
            not rom(oam, 8) or not rom(subsprites, 48) or not rom(images, playerImages[graphics] * 8) then return end
        return {oam = oam, images = images, subsprites = subsprites, palette = core:read16(info + 2),
            width = width, height = height, count = playerImages[graphics]}
    end

    local function cameraOrigin(core, save, total, movement)
        -- Inverse of Ruby's sub_80603CC, including the in-progress camera step.
        local step = signed16(core:read16(movement))
        local adjustment = step > 0 and 16 or (step < 0 and -16 or 0)
        return signed16((7 - signed16(core:read16(save))) * 16 - core:read16(total) - step + adjustment)
    end

    local function field(core)
        if not isRuby(core) or not profile.fieldCallbacks[core:read32(profile.main + 4) & ~1] or
            (core:read8(profile.main + 0x43D) & 2) ~= 0 then return end
        local objectId, spriteId = core:read8(profile.avatar + 5), core:read8(profile.avatar + 4)
        if objectId >= 16 or spriteId >= 64 then return end
        local object, sprite = profile.objects + objectId * 36, sprites + spriteId * spriteSize
        local layout = core:read32(profile.mapHeader)
        if not rom(layout, 24) or (core:read8(object) & 1) == 0 or (core:read8(object + 2) & 1) == 0 or
            core:read8(object + 4) ~= spriteId or (core:read8(sprite + 0x3E) & 3) ~= 3 then return end
        local width, height = core:read32(layout), core:read32(layout + 4)
        if width < 1 or width > 1024 or height < 1 or height > 1024 then return end
        return {object = object, sprite = sprite, objectId = objectId, width = width, height = height,
            group = core:read8(profile.save1 + 4), number = core:read8(profile.save1 + 5),
            x = cameraOrigin(core, profile.save1, totalX, camera + 0x10),
            y = cameraOrigin(core, profile.save1 + 2, totalY, camera + 0x14),
            ix = signed16(core:read16(coordOffsetX)), iy = signed16(core:read16(coordOffsetY))}
    end

    local function animationImage(core, sprite, count)
        local anim, command = core:read8(sprite + 0x2A), core:read8(sprite + 0x2B)
        local animations = core:read32(sprite + 8)
        if anim > 63 or command > 63 or not rom(animations, (anim + 1) * 4) then return end
        local frames = core:read32(animations + anim * 4)
        if not rom(frames, (command + 1) * 4) then return end
        local image = core:read16(frames + command * 4)
        if image < count then return image end
    end

    local function sample(core, localField)
        local object, sprite = localField.object, localField.sprite
        if (core:read8(sprite + 0x3E) & 4) ~= 0 then return end
        local graphics = core:read8(object + 5)
        local info = readGraphics(core, graphics)
        if not info or core:read32(sprite + 0x0C) ~= info.images then return end
        local image = animationImage(core, sprite, info.count)
        if not image then return end

        local x = signed16(core:read16(sprite + 0x20) - localField.x - 8)
        local y = signed16(core:read16(sprite + 0x22) - localField.y - 16 + info.height // 2)
        local ox, oy = signed16(core:read16(sprite + 0x24)), signed16(core:read16(sprite + 0x26))
        if x < -32 or x > localField.width * 16 + 32 or y < -32 or y > localField.height * 16 + 32 or
            math.abs(ox) > 64 or math.abs(oy) > 64 then return end
        local surfImage, surfY, surfFlip = -1, 0, 0
        local effect = core:read8(object + 0x1A)
        if (graphics == 2 or graphics == 92) and effect < 64 then
            local surf = sprites + effect * spriteSize
            if core:read32(surf + 0x1C) == 0x081282F9 and (core:read8(surf + 0x3E) & 5) == 1 and
                core:read16(surf + 0x32) == localField.objectId then
                surfImage = animationImage(core, surf, 3) or -1
                surfY = math.max(-32, math.min(32, signed16(core:read16(surf + 0x26))))
                surfFlip = (core:read16(surf + 2) >> 12) & 3
            end
        end
        return {localField.group, localField.number, x, y, ox, oy, graphics, image,
            (core:read16(sprite + 2) >> 12) & 3, core:read8(object + 0x0B) >> 4,
            (core:read16(sprite + 4) >> 10) & 3, core:read8(sprite + 0x42) & 0x3F, surfImage, surfY, surfFlip}
    end

    local function owned(core, address)
        return (core:read8(address + 0x3E) & 1) ~= 0 and core:read32(address + 0x1C) == dummyCallback and
            core:read16(address + 0x3A) == marker and core:read16(address + 0x3C) == markerTail
    end

    local function markTiles(core, start, count, allocated)
        local mask = ((1 << count) - 1) << (start % 8)
        local address = tileBitmap + start // 8
        for index = 0, (start % 8 + count - 1) // 8 do
            local byte = core:read8(address + index)
            local bits = (mask >> (index * 8)) & 255
            core:write8(address + index, allocated and (byte | bits) or (byte & ~bits))
        end
    end

    local function release(core, address)
        if not owned(core, address) then return end
        local first = core:read16(address + 0x40)
        local count = core:read16(address + 0x38) == 32 and 32 or tileCount
        if first >= core:read16(reservedTiles) and first <= 1024 - count then markTiles(core, first, count, false) end
        copyWords(core, dummy, address, spriteSize)
    end

    local function palettes(core, used)
        for slot = core:read8(reservedPalettes), 15 do
            local tag = core:read16(paletteTags + slot * 2)
            if tag >= paletteMarker and tag < paletteMarker + 16 and not used[slot] then
                core:write16(paletteTags + slot * 2, 0xFFFF)
                paletteKeys[slot] = nil
            end
        end
    end

    local function restoreLocalTint(core)
        if not localTint then return end
        local address = localTint.sprite
        local attr = core:read16(address + 4)
        if (attr >> 12) == localTint.slot and core:read16(paletteTags + localTint.slot * 2) == paletteMarker + localTint.slot and
            (core:read8(address + 0x3E) & 1) == 1 and core:read32(address + 0x0C) == localTint.images and
            core:read32(address + 0x1C) == localTint.callback then
            core:write16(address + 4, (attr & 0x0FFF) | (localTint.original << 12))
        end
        localTint = nil
    end

    local function clear(core)
        restoreLocalTint(core)
        for index = 0, 63 do release(core, sprites + index * spriteSize) end
        palettes(core, {})
        parts, paletteKeys, cleared = {}, {}, true
    end

    local function allocate(core, count)
        local address
        for index = 63, 0, -1 do
            local candidate = sprites + index * spriteSize
            if (core:read8(candidate + 0x3E) & 1) == 0 then address = candidate; break end
        end
        if not address then return end

        local consecutive = 0
        for tile = 1023, core:read16(reservedTiles), -1 do
            if (core:read8(tileBitmap + tile // 8) & (1 << (tile % 8))) == 0 then
                consecutive = consecutive + 1
                if consecutive == count then
                    markTiles(core, tile, count, true)
                    copyWords(core, dummy, address, spriteSize)
                    core:write8(address + 0x2C, 0xC0) -- Animation is driven by the received frame.
                    core:write16(address + 0x38, count)
                    core:write16(address + 0x3A, marker)
                    core:write16(address + 0x3C, markerTail)
                    core:write16(address + 0x3E, 0x7003) -- In use, camera relative, paused sheet sprite.
                    core:write16(address + 0x40, tile)
                    return {address = address, tile = tile}
                end
            else
                consecutive = 0
            end
        end
    end

    local function replaceRed(bgr, color)
        local red, green, blue = bgr & 31, (bgr >> 5) & 31, (bgr >> 10) & 31
        if red < 8 or red - green < 5 or red - blue < 5 or green * 5 > red * 3 then return bgr end
        local function shade(component) return ((component & 255) * red + 127) // 255 end
        return shade(color >> 16) | (shade(color >> 8) << 5) | (shade(color) << 10)
    end

    local function palette(core, localField, tag, color, strict)
        local localInfo = readGraphics(core, core:read8(localField.object + 5))
        local localSlot = localTint and localTint.original or (core:read16(localField.sprite + 4) >> 12)
        if not color and localInfo and localInfo.palette == tag then return localSlot end
        if tag ~= 0x1100 and tag ~= 0x1110 then return end
        local key, free = tag .. ":" .. tostring(color), nil
        for slot = 15, core:read8(reservedPalettes), -1 do
            local entry = core:read16(paletteTags + slot * 2)
            if entry == paletteMarker + slot and paletteKeys[slot] == key then return slot end
            if entry == 0xFFFF and not free then free = slot end
        end
        if not free then
            if strict then return end
            if color then return palette(core, localField, tag, nil) end
            if localInfo then return localSlot end
            return
        end
        for index = 0, 27 do
            local entry = objectPalettes + index * 8
            if core:read16(entry + 4) == tag then
                local colors = core:read32(entry)
                if not rom(colors, 32) then return end
                for offset = 0, 30, 2 do
                    local bgr = core:read16(colors + offset)
                    -- Both Ruby trainer palettes reserve entries 12 and 13 for the red clothing shades.
                    if color and (offset == 24 or offset == 26) then bgr = replaceRed(bgr, color) end
                    core:write16(unfaded + free * 32 + offset, bgr)
                    core:write16(faded + free * 32 + offset, bgr)
                end
                core:write16(paletteTags + free * 2, paletteMarker + free)
                paletteKeys[free] = key
                return free
            end
        end
    end

    local function tintLocalPlayer(core, localField)
        if not localColor then return end
        local address = localField.sprite
        local info = readGraphics(core, core:read8(localField.object + 5))
        if not info or core:read32(address + 0x0C) ~= info.images then return end
        local slot = palette(core, localField, info.palette, localColor, true)
        if not slot then return end
        local attr = core:read16(address + 4)
        localTint = {sprite = address, images = info.images, callback = core:read32(address + 0x1C), slot = slot, original = attr >> 12}
        core:write16(address + 4, (attr & 0x0FFF) | (slot << 12))
        return slot
    end

    local function draw(core, key, info, image, x, y, ox, oy, flip, paletteSlot, priority, tableIndex, subpriority)
        if image < 0 or image >= info.count then return end
        local frame = info.images + image * 8
        local pixels, size = core:read32(frame), core:read16(frame + 4)
        if size < info.width * info.height // 2 or size > tileCount * 32 or size % 32 ~= 0 or not rom(pixels, size) then return end
        local part = parts[key]
        if not part or not owned(core, part.address) then part = allocate(core, tileCount) end
        if not part then return end
        parts[key] = part

        local sprite = part.address
        if part.pixels ~= pixels then
            copyWords(core, pixels, objTiles + part.tile * 32, size)
            part.pixels = pixels
        end
        core:write16(sprite, core:read16(info.oam) & 0xFC00)
        core:write16(sprite + 2, (core:read16(info.oam + 2) & 0xC000) | (flip << 12))
        core:write16(sprite + 4, part.tile | (priority << 10) | (paletteSlot << 12))
        core:write32(sprite + 0x18, info.subsprites or 0)
        core:write16(sprite + 0x20, x & 0xFFFF)
        core:write16(sprite + 0x22, y & 0xFFFF)
        core:write16(sprite + 0x24, ox & 0xFFFF)
        core:write16(sprite + 0x26, oy & 0xFFFF)
        core:write16(sprite + 0x28, ((-info.height // 2) & 255) << 8 | ((-info.width // 2) & 255))
        core:write8(sprite + 0x42, info.subsprites and (0x40 | tableIndex) or 0)
        core:write8(sprite + 0x43, subpriority)
        return true
    end

    local function interpolate(track, tick)
        local target = track.target
        if not track.source then return target end
        local progress = math.min(1, (tick - track.at) / track.duration)
        if progress >= 1 then return target end
        local pose = {table.unpack(progress < 0.5 and track.source or target)}
        for index = 3, 6 do
            pose[index] = math.floor(track.source[index] + (target[index] - track.source[index]) * progress + 0.5)
        end
        pose[14] = math.floor(track.source[14] + (target[14] - track.source[14]) * progress + 0.5)
        return pose
    end

    local function labelPalette(core, localField)
        local colors, indexes = {}, {}
        for _, track in pairs(tracks) do
            local pose = track.target
            if pose[1] == localField.group and pose[2] == localField.number and pose.label and #pose.label > 0 and not indexes[pose.color] then
                colors[#colors + 1] = pose.color
                indexes[pose.color] = true
            end
        end
        if #colors == 0 then return end
        table.sort(colors)
        local key = "label:" .. table.concat(colors, ",")
        local slot
        for candidate = 15, core:read8(reservedPalettes), -1 do
            local tag = core:read16(paletteTags + candidate * 2)
            if tag == paletteMarker + candidate and paletteKeys[candidate] and paletteKeys[candidate]:sub(1, 6) == "label:" then
                slot = candidate
                break
            end
            if tag == 0xFFFF and not slot then slot = candidate end
        end
        if not slot then return end
        local function write(index, bgr)
            core:write16(unfaded + slot * 32 + index * 2, bgr)
            core:write16(faded + slot * 32 + index * 2, bgr)
        end
        if paletteKeys[slot] ~= key then
            write(0, 0)
            write(1, 0x1062)
            write(2, 0x7FFF)
            for index, rgb in ipairs(colors) do
                write(index + 2, ((rgb >> 19) & 31) | (((rgb >> 11) & 31) << 5) | (((rgb >> 3) & 31) << 10))
            end
            core:write16(paletteTags + slot * 2, paletteMarker + slot)
            paletteKeys[slot] = key
        end
        for index, rgb in ipairs(colors) do indexes[rgb] = index + 2 end
        return slot, indexes
    end

    local function drawLabel(core, key, localField, text, color, x, y, slot, backing)
        local part = parts[key]
        if not part or not owned(core, part.address) then part = allocate(core, 32) end
        if not part then return end
        parts[key] = part
        local width = #text * 4 + 3
        local appearance = text .. ":" .. color .. ":" .. backing
        if part.pixels ~= appearance then
            local brightness = ((color >> 16) & 255) * 299 + ((color >> 8) & 255) * 587 + (color & 255) * 114
            local ink = brightness >= 128000 and 1 or 2
            local function pixel(px, py)
                if px >= width or py > 8 then return 0 end
                local edge = px == 0 or px == width - 1
                if py == 0 or py == 8 then return edge and 0 or 1 end
                if edge then return 1 end
                if px >= 2 and py >= 2 and py <= 6 then
                    local column, letter = (px - 2) % 4, (px - 2) // 4 + 1
                    local rows = glyphs[text:sub(letter, letter)] or glyphs["?"]
                    if letter <= #text and column < 3 and (rows[py - 1] & (1 << (2 - column))) ~= 0 then return ink end
                end
                return backing
            end
            local destination = objTiles + part.tile * 32
            for tile = 0, 31 do
                for row = 0, 7 do
                    local word = 0
                    for column = 0, 7 do
                        word = word | (pixel((tile % 8) * 8 + column, (tile // 8) * 8 + row) << (column * 4))
                    end
                    core:write32(destination + tile * 32 + row * 4, word)
                end
            end
            part.pixels = appearance
        end

        local sprite = part.address
        local left = math.max(0, math.min(240 - width, x - width // 2))
        local top = math.max(0, math.min(151, y))
        core:write16(sprite, 0x4000) -- Horizontal 64x32 OBJ, top layer.
        core:write16(sprite + 2, 0xC000)
        core:write16(sprite + 4, part.tile | (slot << 12))
        core:write16(sprite + 0x20, (left + 32 - localField.ix) & 0xFFFF)
        core:write16(sprite + 0x22, (top + 16 - localField.iy) & 0xFFFF)
        core:write16(sprite + 0x28, 0xF0E0)
        core:write8(sprite + 0x43, 0)
        return true
    end

    local function update(line, tick)
        -- Field order matches OverworldPose. Network records never carry graphics or memory addresses.
        local incoming, count = {}, 0
        local duration = lastPacket and math.max(3, math.min(10, tick - lastPacket)) or 4
        for record in line:gmatch("%S+") do
            local key, payload = record:match("^(%x+):(.+)$")
            if not key or #key ~= 32 or count == 11 or incoming[key] then return end
            local values, tint, label = payload:match("^([%d:%-]+):(%x%x%x%x%x%x):(%x*)$")
            values = values or payload:match("^([%d:%-]+)$")
            if not values or (label and (#label > 24 or #label % 2 ~= 0)) then return end
            local pose = {}
            for value in values:gmatch("[^:]+") do
                local number = math.tointeger(tonumber(value))
                if not number or #pose == 15 then return end
                pose[#pose + 1] = number
            end
            if #pose ~= 15 or not playerImages[pose[7]] or pose[8] < 0 or pose[8] >= playerImages[pose[7]] or
                pose[9] < 0 or pose[9] > 3 or pose[10] < 0 or pose[10] > 15 or pose[11] < 0 or pose[11] > 3 or
                pose[12] < 0 or pose[12] > 5 or pose[13] < -1 or pose[13] > 2 or pose[15] < 0 or pose[15] > 3 then return end
            if tint then
                pose.color = tonumber(tint, 16)
                pose.label = label:gsub("%x%x", function(pair) return string.char(tonumber(pair, 16)) end)
            end
            local previous = tracks[key]
            local source = previous and interpolate(previous, tick)
            if source and (source[1] ~= pose[1] or source[2] ~= pose[2] or
                math.abs(source[3] - pose[3]) + math.abs(source[4] - pose[4]) > 48) then source = nil end
            incoming[key] = {source = source, target = pose, at = tick, duration = duration}
            count = count + 1
        end
        tracks, lastPacket, lastPacketAt = incoming, tick, os.time()
    end

    local function render(core, localField, tick)
        if not cleared then clear(core) end
        if not lastPacket or tick - lastPacket > 75 or os.time() - lastPacketAt >= 2 then tracks = {} end
        if not localColor and not localTint and not next(tracks) and not next(parts) then return end
        local visible, used = {}, {}
        restoreLocalTint(core)
        local localSlot = tintLocalPlayer(core, localField)
        if localSlot then used[localSlot] = true end
        local labelSlot, labelColors
        if showLabels then labelSlot, labelColors = labelPalette(core, localField) end
        if labelSlot then used[labelSlot] = true end
        for key, track in pairs(tracks) do
            local p = interpolate(track, tick)
            if p[1] == localField.group and p[2] == localField.number and
                p[3] >= -32 and p[3] <= localField.width * 16 + 32 and p[4] >= -32 and p[4] <= localField.height * 16 + 32 and
                math.abs(p[5]) <= 64 and math.abs(p[6]) <= 64 and math.abs(p[14]) <= 32 then
                local x, y = signed16(localField.x + p[3] + 8), signed16(localField.y + p[4] + 16)
                local screenX, screenY = signed16(x + localField.ix), signed16(y + localField.iy)
                if screenX >= -48 and screenX < 288 and screenY >= -48 and screenY < 208 then
                    local info = readGraphics(core, p[7])
                    local slot = info and palette(core, localField, info.palette, track.target.color)
                    local subpriority = (16 - (((screenY + 8) & 255) >> 4)) * 2 + depth[p[10] + 1] + 1
                    if slot and draw(core, key, info, p[8], x, y - info.height // 2, p[5], p[6], p[9], slot, p[11], p[12], subpriority) then
                        visible[key], used[slot] = true, true
                        local label = track.target.label
                        if labelSlot and label and #label > 0 and screenX > 0 and screenX < 240 and screenY > 0 and screenY < 160 and
                            drawLabel(core, key .. "n", localField, label, track.target.color, screenX + p[5],
                                screenY - info.height + p[6] - 3, labelSlot, labelColors[track.target.color]) then
                            visible[key .. "n"] = true
                        end
                        if p[13] >= 0 and (p[7] == 2 or p[7] == 92) then
                            local template = core:read32(surfTemplates + 7 * 4)
                            if rom(template, 24) then
                                local surf = {width = 32, height = 32, count = 3, oam = core:read32(template + 4), images = core:read32(template + 12)}
                                if rom(surf.oam, 8) and rom(surf.images, 24) and draw(core, key .. "s", surf, p[13], x,
                                    y - info.height // 2 + 8, 0, p[14], p[15], slot, p[11], 0, 0x96) then visible[key .. "s"] = true end
                            end
                        end
                    end
                end
            end
        end
        for key, part in pairs(parts) do
            if not visible[key] then release(core, part.address); parts[key] = nil end
        end
        palettes(core, used)
    end

    local format = '{"mapGroup":%d,"mapNumber":%d,"x":%d,"y":%d,"offsetX":%d,"offsetY":%d,"graphics":%d,"image":%d,' ..
        '"flip":%d,"elevation":%d,"priority":%d,"subsprite":%d,"surfImage":%d,"surfOffsetY":%d,"surfFlip":%d}'
    return {
        update = update,
        style = function(color, labels) localColor, showLabels = color, labels end,
        step = function(tick, capture)
            local localField = field(core)
            if not localField then
                if (localTint or next(parts)) and isRuby(core) then clear(core) end
                if capture then return "null" end
                return
            end
            render(core, localField, tick)
            if capture then
                local pose = sample(core, localField)
                return pose and string.format(format, table.unpack(pose)) or "null"
            end
        end,
        hide = function()
            if isRuby(core) then clear(core) end
        end,
        reset = function()
            if isRuby(core) then clear(core) end
            tracks, parts, lastPacket, lastPacketAt, cleared = {}, {}, nil, nil, false
            localTint, localColor, showLabels = nil, nil, true
        end,
    }
end
