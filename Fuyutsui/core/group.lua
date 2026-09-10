local addon, ns = ...

local state = Fuyutsui.state
local roleMap = Fuyutsui.roleMap
local updateIndex = 1

local function IsSecret(value)
    return type(issecretvalue) == "function" and issecretvalue(value)
end

local function ResolveUnitInRange(unit)
    if unit == "player" then
        return true
    end

    local ok, _, maxRange = pcall(function()
        return Fuyutsui:GetUnitRange(unit)
    end)
    if not ok or IsSecret(maxRange) or type(maxRange) ~= "number" then
        return false
    end

    return maxRange <= 40
end

local function Clamp(value, low, high)
    return math.max(low, math.min(high, value))
end

local function HasVirtueAura(unit)
    if C_UnitAuras and C_UnitAuras.GetAuraDataBySpellId then
        local ok, aura = pcall(C_UnitAuras.GetAuraDataBySpellId, unit, 200025)
        return ok and aura ~= nil
    end
    if UnitAura then
        for index = 1, 40 do
            local ok, _, _, _, _, _, _, _, _, _, _, spellId = pcall(
                UnitAura,
                unit,
                index,
                "HELPFUL"
            )
            if ok and not IsSecret(spellId) and spellId == 200025 then
                return true
            end
        end
    end
    return false
end

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
    local inRaid = not forceParty and self:IsRaidGroup()
    local unit = inRaid and 'raid' or 'party'
    local numGroupMembers = inRaid and GetNumGroupMembers() or GetNumSubgroupMembers()
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
    if not healthPercent or type(healthPercent.GetRGB) ~= "function" then
        return
    end
    ---@diagnostic disable-next-line: param-type-mismatch
    local ok, _, _, b = pcall(healthPercent.GetRGB, healthPercent)
    if not ok then
        return
    end
    obj.healthPercent = b
    obj.healthPercentValue = IsSecret(b) and 1 or b
    self:CreateTexture(index, b)
end

function Fuyutsui:UpdateHolyPaladinForecast()
    local blocks = self.blocks
    if not blocks or not blocks.groups then return end

    local now = GetTime()
    local singleNeed, burstNeed, sustainNeed, spreadCount = 0, 0, 0, 0
    local virtueCoverageCount, virtueTransferNeed = 0, 0
    local virtueMainTarget = state.virtueMainTargetIndex or 0

    for _, unit in ipairs(self.groupList or {}) do
        local obj = self.group[unit]
        if obj and obj.valid then
            local health = Clamp((obj.healthPercentValue or 1) * 100, 0, 100)
            local deficit = math.max(0, 100 - health)
            local elapsed = math.max(0.1, now - (obj.forecastAt or now))
            local previous = obj.forecastHealth or health
            local observedDamage = math.max(0, previous - health)
            local observedRate = observedDamage / elapsed
            obj.damageRate = Clamp((obj.damageRate or 0) * 0.65 + observedRate * 0.35, 0, 18)
            obj.forecastAt = now
            obj.forecastHealth = health

            local safety = obj.role == "TANK" and 8 or 5
            -- A warning reserves resources; it is not damage to every member.
            local shortPrediction = Clamp(obj.damageRate * 2, 0, 80)
            local longPrediction = Clamp(obj.damageRate * 9, 0, 140)
            local expected = Clamp(deficit + longPrediction + safety, 0, 255)
            local burst = Clamp(deficit + shortPrediction, 0, 255)
            local sustain = Clamp(deficit + longPrediction, 0, 255)
            local single = Clamp(deficit + shortPrediction + safety, 0, 255)

            obj.expectedNeed, obj.burstNeed = expected, burst
            obj.sustainNeed, obj.singleNeed = sustain, single

            local base = blocks.groups.start + (obj.index - 1) * blocks.groups.num
            if blocks.groups.expectedNeed then self:CreateTexture(base + blocks.groups.expectedNeed, expected / 255) end
            if blocks.groups.burstNeed then self:CreateTexture(base + blocks.groups.burstNeed, burst / 255) end
            if blocks.groups.sustainNeed then self:CreateTexture(base + blocks.groups.sustainNeed, sustain / 255) end

            singleNeed = math.max(singleNeed, single)
            burstNeed = burstNeed + (burst >= 15 and burst or 0)
            sustainNeed = sustainNeed + (sustain >= 15 and sustain or 0)
            if expected >= 15 then spreadCount = spreadCount + 1 end

            local hasVirtue = HasVirtueAura(unit)
            if hasVirtue then
                virtueCoverageCount = virtueCoverageCount + 1
                if obj.index ~= virtueMainTarget then
                    virtueTransferNeed = virtueTransferNeed + math.floor(expected * 0.15 + 0.5)
                end
            end
        end
    end

    state.singleNeed = Clamp(singleNeed, 0, 255)
    state.burstGroupNeed = Clamp(burstNeed, 0, 255)
    state.sustainGroupNeed = Clamp(sustainNeed, 0, 255)
    state.spreadCount = Clamp(spreadCount, 0, 255)
    state.virtueCoverageCount = Clamp(virtueCoverageCount, 0, 255)
    state.virtueTransferNeed = Clamp(virtueTransferNeed, 0, 255)
    state.virtueCoverageOverflow = math.max(0, virtueCoverageCount - 5)

    local fields = {
        "单目标需求", "多人爆发需求", "多人持续需求", "预计治疗人数",
        "美德主目标", "美德覆盖人数", "美德转移需求", "美德覆盖溢出",
    }
    for _, field in ipairs(fields) do
        self:UpdateStateBlock("状态", field)
    end
end

function Fuyutsui:UpdateUnitValid(unit)
    local blocks = self.blocks
    local obj = self.group[unit]
    if not obj then return end
    obj.inRange = ResolveUnitInRange(unit)
    obj.valid = not obj.isDead and obj.canAssist and obj.inSight and obj.inRange
    if blocks and blocks.groups and blocks.groups.canHeal then
        local index = blocks.groups.start + (obj.index - 1) * blocks.groups.num + blocks.groups.canHeal
        self:CreateTexture(index, obj.valid and 1 / 255 or 0)
    end
end

function Fuyutsui:UpdateGroupInRangeAndHealth()
    local blocks = self.blocks
    local group = self.group
    local groupList = self.groupList
    if not blocks or not blocks.groups then return end
    local numUnits = #groupList
    if numUnits >= 1 then
        if updateIndex > numUnits then
            updateIndex = 1
        end
        local unit = groupList[updateIndex]
        local obj = group[unit]
        if not obj then return end
        local index = blocks.groups.start + (obj.index - 1) * blocks.groups.num + blocks.groups.role
        obj.isDead = UnitIsDeadOrGhost(unit)
        obj.canAssist = UnitCanAssist("player", unit)
        self:UpdateUnitValid(unit)
        if obj.valid then
            self:UpdateUnitHealthInfo(unit)
            -- 可治疗成员保留职责标记；无职责的有效成员使用稳定占位值。
            local roleValue = roleMap[obj.role]
            if not roleValue or roleValue == 0 then
                roleValue = 5
            end
            self:CreateTexture(index, roleValue / 255)
        else
            local healthIndex = blocks.groups.start + (obj.index - 1) * blocks.groups.num + blocks.groups.healthPercent
            obj.healthPercent = 0
            obj.healthPercentValue = 0
            obj.forecastHealth = 0
            obj.damageRate = 0
            obj.expectedNeed, obj.burstNeed = 0, 0
            obj.sustainNeed, obj.singleNeed = 0, 0
            self:CreateTexture(healthIndex, 0)
            self:CreateTexture(index, 0)
            if blocks.groups.expectedNeed then self:CreateTexture(blocks.groups.start + (obj.index - 1) * blocks.groups.num + blocks.groups.expectedNeed, 0) end
            if blocks.groups.burstNeed then self:CreateTexture(blocks.groups.start + (obj.index - 1) * blocks.groups.num + blocks.groups.burstNeed, 0) end
            if blocks.groups.sustainNeed then self:CreateTexture(blocks.groups.start + (obj.index - 1) * blocks.groups.num + blocks.groups.sustainNeed, 0) end
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
    if blocks.groups and blocks.groups.start and blocks.groups.num then
        local startIndex = blocks.groups.start
        local endIndex = startIndex + 30 * blocks.groups.num - 1
        for index = startIndex, endIndex do
            self:CreateTexture(index, 0)
        end
    end
end

function Fuyutsui:UpdateGroup()
    self.group = {}
    self.groupList = {}
    updateIndex = 1
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
            inRange = ResolveUnitInRange(unit),
            canAttack = UnitCanAttack("player", unit),
            canAssist = UnitCanAssist("player", unit),
            inSight = true,
            inSightTimer = nil,
            curve = self.curve100,
            inComingHeals = 0,
            forecastAt = GetTime(),
            forecastHealth = 100,
            damageRate = 0,
        }
        self:UpdateUnitValid(unit)
        self:UpdateUnitHealthInfo(unit)
        group[unit].forecastHealth = (group[unit].healthPercentValue or 1) * 100
        i = i + 1
    end
    if self.RefreshGroupAuraContainers then
        self:RefreshGroupAuraContainers()
    end
    if self.RefreshGroupHealAbsorbBars then
        self:RefreshGroupHealAbsorbBars()
    end
    self:UpdateHolyPaladinForecast()
end
