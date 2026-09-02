local addon, ns = ...
local format = string.format
local macroList = {}
local macroKind = {}
local bindingOwner = CreateFrame("Frame")
local routeRouter
local routeTargets = {}
local SELECTOR_TARGET_ROUTING = "selector-target-v1"

local function publishMacroStatus(status, count)
    Fuyutsui.state.macroBindingStatus = status
    Fuyutsui.state.macroBindingCount = count or 0
    Fuyutsui:UpdateStateBlock("状态", "宏绑定状态")
    Fuyutsui:UpdateStateBlock("状态", "宏绑定数量")
end
local modifiers = {
    "CTRL", "ALT", "SHIFT",
    "ALT-CTRL", "ALT-SHIFT", "CTRL-SHIFT",
    "ALT-CTRL-SHIFT"
}

local keys = {
    "NUMPAD1", "NUMPAD2", "NUMPAD3", "NUMPAD4", "NUMPAD5",
    "NUMPAD6", "NUMPAD7", "NUMPAD8", "NUMPAD9", "NUMPAD0",
    "NUMPADDECIMAL", "NUMPADPLUS", "NUMPADMINUS", "NUMPADMULTIPLY", "NUMPADDIVIDE",
    "F1", "F2", "F3", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
    ",", ".", "/", ";", "'", "[", "]", "\\",
    "7", "8", "9", "0", "="
}

do
    local i = 1
    for _, m in ipairs(modifiers) do
        for _, k in ipairs(keys) do
            macroKind[i] = m .. "-" .. k
            i = i + 1
        end
    end
end


local function createAction(name, macro)
    if InCombatLockdown() then
        return nil
    end
    local btn = macroList[name]
    if not btn then
        btn = CreateFrame("Button", name, UIParent, "SecureActionButtonTemplate")
        btn:SetAttribute("type", "macro")
        -- WoW may dispatch SetOverrideBindingClick on either keyboard edge.
        -- Keep both edges registered because the desktop client posts down/up.
        btn:RegisterForClicks("AnyUp", "AnyDown")
        macroList[name] = btn
    end
    btn:SetAttribute("macrotext", macro)
    return btn
end

local function createMacro(name, key, macro)
    local btn = createAction(name, macro)
    if btn then
        SetOverrideBindingClick(bindingOwner, true, key, name, "LeftButton")
    end
end

local function findTankUnit()
    if IsInRaid() then
        for raidIdx = 1, GetNumGroupMembers() do
            local unit = "raid" .. raidIdx
            if UnitGroupRolesAssigned(unit) == "TANK" then
                return unit
            end
        end
    elseif IsInGroup() then
        if UnitGroupRolesAssigned("player") == "TANK" then
            return "player"
        end
        for partyIdx = 1, GetNumSubgroupMembers() do
            local unit = "party" .. partyIdx
            if UnitGroupRolesAssigned(unit) == "TANK" then
                return unit
            end
        end
    end
    return nil
end

local function buildTankTargetMacro(spell)
    local tankUnit = findTankUnit()
    if not tankUnit then
        return format("/cast [@targettarget,harm,nodead][harm,nodead]%s", spell)
    end
    return format(
        "/cast [@%starget,harm,nodead][@targettarget,harm,nodead][harm,nodead]%s",
        tankUnit,
        spell)
end

-- 解析法术名/宏体：优先查 MacroBodies；以 / 开头则原样使用；否则加 /cast
local function resolveMacroBody(spell)
    if not spell or spell == "" then
        return nil
    end
    local bodies = Fuyutsui.MacroBodies
    local body = bodies and bodies[spell]
    if body then
        if body:sub(1, 1) == "/" then
            return body
        end
        return "/cast " .. body
    end
    if spell:sub(1, 1) == "/" then
        return spell
    end
    local tankTargetSpell = spell:match("^%[@tanktarget%](.+)$")
    if tankTargetSpell then
        return buildTankTargetMacro(tankTargetSpell)
    end
    return "/cast " .. spell
end

function Fuyutsui:ClearMacros()
    if InCombatLockdown() then
        return false
    end
    ClearOverrideBindings(bindingOwner)
    for _, btn in pairs(macroList) do
        btn:SetAttribute("macrotext", nil)
    end
    for _, btn in pairs(routeTargets) do
        btn:SetAttribute("macrotext", nil)
        btn:SetAttribute("clickbutton", nil)
    end
    return true
end

local function buildDynamicMacro(spell, raidIdx)
    if not spell or spell == "" then return nil end
    if raidIdx == 1 then
        return format("/cast [group:raid,@raid1]%s;[group:party,@player]%s;[nogroup,@player]%s", spell, spell,
            spell)
    elseif raidIdx <= 5 then
        return format("/cast [group:raid,@raid%d]%s;[group:party,@party%d]%s", raidIdx, spell, raidIdx - 1,
            spell)
    end
    return format("/cast [group:raid,@raid%d]%s", raidIdx, spell)
end

local function createSelectorTargetMacros(dynamicData, keyOffset)
    if #dynamicData == 0 then
        return 1 + keyOffset
    end

    if not routeRouter then
        routeRouter = CreateFrame("Button", "FuyutsuiMacroRouter", UIParent, "SecureHandlerClickTemplate")
        routeRouter:RegisterForClicks("AnyUp", "AnyDown")
    end

    local selectorStart = 1 + keyOffset
    local targetStart = selectorStart + #dynamicData
    local snippets = {}

    for raidIdx = 1, 30 do
        local targetKey = macroKind[targetStart + raidIdx - 1]
        if targetKey then
            local targetName = format("FuyutsuiRouteTarget%d", raidIdx)
            local target = routeTargets[raidIdx]
            if not target then
                target = CreateFrame("Button", targetName, UIParent, "SecureActionButtonTemplate")
                target:RegisterForClicks("AnyUp", "AnyDown")
                routeTargets[raidIdx] = target
            end
            target:SetAttribute("type", "macro")
            target:SetAttribute("macrotext", nil)
            target:SetAttribute("clickbutton", nil)
            routeRouter:SetFrameRef("t" .. raidIdx, target)
            SetOverrideBindingClick(bindingOwner, true, targetKey, targetName, "LeftButton")
        end
    end

    for spellIndex, spell in ipairs(dynamicData) do
        local selectorKey = macroKind[selectorStart + spellIndex - 1]
        if selectorKey and spell and spell ~= "" then
            SetOverrideBindingClick(bindingOwner, true, selectorKey, routeRouter:GetName(), "Spell" .. spellIndex)
            snippets[#snippets + 1] = spellIndex == 1 and
                "if button == 'Spell1' then" or
                format("elseif button == 'Spell%d' then", spellIndex)

            for raidIdx = 1, 30 do
                local target = routeTargets[raidIdx]
                if target then
                    local macroAttribute = format("route-macro-%d-%d", spellIndex, raidIdx)
                    routeRouter:SetAttribute(macroAttribute, buildDynamicMacro(spell, raidIdx))
                    snippets[#snippets + 1] = format(
                        "self:GetFrameRef('t%d'):SetAttribute('macrotext', self:GetAttribute('%s'))",
                        raidIdx,
                        macroAttribute)
                end
            end
        end
    end
    snippets[#snippets + 1] = "end"
    routeRouter:SetAttribute("_onclick", table.concat(snippets, "\n"))
    return targetStart + 30
end

function Fuyutsui:CreateMacro(dynamicData, staticData, specialData, keyOffset, routingMode)
    if InCombatLockdown() then
        publishMacroStatus(2, 0)
        return false
    end

    dynamicData = dynamicData or {}
    staticData = staticData or {}
    specialData = specialData or {}

    if not self:ClearMacros() then
        publishMacroStatus(3, 0)
        return false
    end

    local offset = keyOffset or 0
    local i = 1 + offset
    local function nextSlot(macroBody)
        local keyBinding = macroKind[i]
        if not keyBinding then
            return
        end
        if macroBody then
            createMacro("s" .. i, keyBinding, macroBody)
        end
        i = i + 1
    end

    -- 1. dynamicSpells：兼容旧版每技能 30 键；两段路由只占技能选择键 + 30 个单位键。
    if routingMode == SELECTOR_TARGET_ROUTING then
        i = createSelectorTargetMacros(dynamicData, offset)
    else
        for _, spell in ipairs(dynamicData) do
            for raidIdx = 1, 30 do
                nextSlot(buildDynamicMacro(spell, raidIdx))
            end
        end
    end

    -- 2. staticSpells：依次占键；空字符串保留占位但不创建
    for _, spell in ipairs(staticData) do
        nextSlot(resolveMacroBody(spell))
    end

    -- 3. specialSpells：完整宏文本，接在 static 之后依次占键
    for _, spell in ipairs(specialData) do
        nextSlot(resolveMacroBody(spell))
    end

    local dynamicSlots = routingMode == SELECTOR_TARGET_ROUTING
        and (#dynamicData > 0 and (#dynamicData + 30) or 0)
        or (#dynamicData * 30)
    local staticSlots = 0
    for _, spell in ipairs(staticData) do
        if spell and spell ~= "" then staticSlots = staticSlots + 1 end
    end
    for _, spell in ipairs(specialData) do
        if spell and spell ~= "" then staticSlots = staticSlots + 1 end
    end
    publishMacroStatus(1, dynamicSlots + staticSlots)
    return true
end
