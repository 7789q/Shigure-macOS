local addon, ns = ...

local EvaluateColorFromBoolean = C_CurveUtil.EvaluateColorFromBoolean

local state = Fuyutsui.state
local roleMap = Fuyutsui.roleMap
local ColorValue0 = CreateColor(0, 0, 0, 1)
local updateIndex = 1

-- 施法治疗预估偏移（近似秒数/权重，写入生命曲线）
local helpfulSpells = {
    [2061] = 15,
    [1262763] = 15,
    [82326] = 40,
    [19750] = 15,
    [8936] = 15,
    [186263] = 40,
    [77472] = 15,
}

function Fuyutsui:IterateGroupMembers(reversed, forceParty)
    local unit = (not forceParty and IsInRaid()) and 'raid' or 'party'
    local numGroupMembers = unit == 'party' and GetNumSubgroupMembers() or GetNumGroupMembers()
    local i = reversed and numGroupMembers or (unit == 'party' and 0 or 1)
    return function()
        local ret
        if i == 0 and unit == 'party' then
            ret = 'player'
        elseif i <= numGroupMembers and i > 0 then
            ret = unit .. i
        end
        i = i + (reversed and -1 or 1)
        return ret
    end
end

function Fuyutsui:UpdateUnitHealthInfo(unit)
    local blocks = self.blocks
    local group = self.group
    local obj = group[unit]
    if not blocks or not blocks.groups or not obj then return end
    local index = blocks.groups.start + (obj.index - 1) * blocks.groups.num + blocks.groups.healthPercent
    obj.curve = self:CreateColorCurveScaling(100 + (obj.inComingHeals or 0))
    local healthPercent = UnitHealthPercent(unit, false, obj.curve)
    ---@diagnostic disable-next-line: param-type-mismatch
    local _, _, b = healthPercent:GetRGB()
    obj.healthPercent = b
    self:CreateTexture(index, obj.healthPercent)
end

function Fuyutsui:UpdateUnitValid(unit)
    local obj = self.group[unit]
    if not obj then return end
    obj.valid = not obj.isDead and obj.canAssist and obj.inSight
end

function Fuyutsui:UpdateGroupInRangeAndHealth()
    local blocks = self.blocks
    local group = self.group
    local groupList = self.groupList
    if not blocks or not blocks.groups then return end
    local numUnits = #groupList
    if numUnits >= 1 then
        local unit = groupList[updateIndex]
        local obj = group[unit]
        if not obj then return end
        self:UpdateUnitHealthInfo(unit)
        local index = blocks.groups.start + (obj.index - 1) * blocks.groups.num + blocks.groups.role
        obj.isDead = UnitIsDeadOrGhost(unit)
        obj.canAssist = UnitCanAssist("player", unit)
        obj.valid = not obj.isDead and obj.canAssist and obj.inSight
        if obj.valid then
            local inRange = UnitIsUnit(unit, "player") and true or UnitInRange(unit)
            local roleValue = roleMap[obj.role] and roleMap[obj.role] / 255 or 5 / 255
            local trueValue = CreateColor(0, 0, roleValue, 1)
            local booleanValue = EvaluateColorFromBoolean(inRange, trueValue, ColorValue0)
            local _, _, b = booleanValue:GetRGB()
            self:CreateTexture(index, b)
        else
            self:CreateTexture(index, 0)
        end
        updateIndex = updateIndex + 1
        if updateIndex > numUnits then
            updateIndex = 1
        end
    end
end

--- source: "guid" | "health" | nil
function Fuyutsui:UpdateUnitDeath(unitOrGuid, source)
    local group = self.group
    if source == "guid" then
        for unit, data in pairs(group) do
            if data.GUID == unitOrGuid then
                data.isDead = true
                self:UpdateUnitValid(unit)
            end
        end
        return
    end

    local obj = group[unitOrGuid]
    if not obj then return end
    obj.isDead = UnitIsDeadOrGhost(unitOrGuid)
    self:UpdateUnitValid(unitOrGuid)
end

function Fuyutsui:UpdateUnitInSight(unit)
    local obj = self.group[unit]
    if not obj then return end
    obj.inSight = false
    if obj.inSightTimer then
        obj.inSightTimer:Cancel()
        obj.inSightTimer = nil
    end
    obj.inSightTimer = C_Timer.NewTimer(1.5, function()
        obj.inSight = true
        obj.inSightTimer = nil
        Fuyutsui:UpdateUnitValid(unit)
    end)
    self:UpdateUnitValid(unit)
end

function Fuyutsui:ApplyIncomingHealsCurve(spellID)
    local unit = state.castTargetUnit
    if not unit then return end
    local obj = self.group[unit]
    if not obj then return end
    local isHelpfulSpell = helpfulSpells[spellID]
    if isHelpfulSpell then
        obj.inComingHeals = isHelpfulSpell
    end
end

function Fuyutsui:UpdateAllIncomingHealsCurves()
    for _, data in pairs(self.group) do
        data.inComingHeals = 0
    end
end

function Fuyutsui:ClearGroupBlocks()
    local blocks = self.blocks
    if blocks.groups and blocks.groups.start then
        local startIndex = blocks.groups.start
        for index = startIndex, 255 do
            self:CreateTexture(index, 0)
        end
    end
end

function Fuyutsui:UpdateGroup()
    self.group = {}
    self.groupList = {}
    local group = self.group
    local groupList = self.groupList
    local i = 1
    for unit in self:IterateGroupMembers() do
        table.insert(groupList, unit)
        local role = UnitGroupRolesAssigned(unit)
        if unit == "player" then
            role = self.state.specRole
        end
        group[unit] = {
            index = i,
            name = GetUnitName(unit, true),
            GUID = UnitGUID(unit),
            role = role,
            isDead = UnitIsDeadOrGhost(unit),
            inRange = UnitInRange(unit),
            canAttack = UnitCanAttack("player", unit),
            canAssist = UnitCanAssist("player", unit),
            inSight = true,
            inSightTimer = nil,
            curve = self.curve100,
            inComingHeals = 0,
        }
        self:UpdateUnitValid(unit)
        self:UpdateUnitHealthInfo(unit)
        i = i + 1
    end
    if self.RefreshGroupAuraContainers then
        self:RefreshGroupAuraContainers()
    end
    if self.RefreshGroupHealAbsorbBars then
        self:RefreshGroupHealAbsorbBars()
    end
end
