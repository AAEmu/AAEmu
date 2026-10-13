-- Trion r208022's TOGGLE_AUCTION samples the mail artwork at (284,352),
-- twenty pixels below its top. right_button_set.alb also hardcodes the Korean
-- tooltip "경매장" (Auction House). Draw original vector artwork with the UI API.
-- No client files or auction actions are changed.
if not UIParent or not ApplyButtonSkin or not SetTooltip or not HideTooltip or
   not BUTTON_HUD or not BUTTON_HUD.TOGGLE_AUCTION then return end
local installed = AAEmuAuctionUiInstalled
if type(installed) == 'table' and installed.apply == ApplyButtonSkin and
   installed.tooltip == SetTooltip and installed.hide == HideTooltip then return 'ready' end

local auctionSkin = BUTTON_HUD.TOGGLE_AUCTION
local hoveredButton

local function tint(button, factor)
  for _, part in ipairs(button.aaemuAuctionParts) do
    local c = part.color
    part.drawable:SetColor(c[1] * factor, c[2] * factor, c[3] * factor, c[4])
  end
end

local function drawAuctionIcon(button)
  for _, bg in ipairs(button.bgs or {}) do bg:SetColor(1, 1, 1, 0) end
  if button.aaemuAuctionParts then return end
  button.aaemuAuctionParts = {}
  local function strip(x, y, width, height, color)
    local d = button:CreateColorDrawable(color[1], color[2], color[3], color[4], 'overlay')
    d:SetExtent(width, height)
    d:AddAnchor('CENTER', button, x, y)
    table.insert(button.aaemuAuctionParts, { drawable = d, color = color })
  end
  local function rectangle(x, y, width, height, color, rotation)
    if not rotation then return strip(x, y, width, height, color) end
    -- Color drawables do not rotate in r208022. Fill the rotated polygon with
    -- horizontal strips using the same native drawables as the medallion.
    local angle = math.rad(rotation)
    local c, s = math.cos(angle), math.sin(angle)
    local corners = {}
    local minY, maxY = math.huge, -math.huge
    for _, p in ipairs({{-width/2,-height/2}, {width/2,-height/2},
                        {width/2,height/2}, {-width/2,height/2}}) do
      local v = {x + p[1]*c - p[2]*s, y + p[1]*s + p[2]*c}
      table.insert(corners, v)
      minY, maxY = math.min(minY, v[2]), math.max(maxY, v[2])
    end
    for row = math.floor(minY), math.ceil(maxY) - 1 do
      local sampleY = row + 0.5
      local left, right = math.huge, -math.huge
      for i = 1, 4 do
        local a, b = corners[i], corners[i % 4 + 1]
        if (a[2] <= sampleY and b[2] > sampleY) or
           (b[2] <= sampleY and a[2] > sampleY) then
          local px = a[1] + (sampleY-a[2]) * (b[1]-a[1]) / (b[2]-a[2])
          left, right = math.min(left, px), math.max(right, px)
        end
      end
      if right > left then strip((left+right)/2, sampleY, right-left, 1, color) end
    end
  end
  local function disk(radius, color)
    for x = -radius + 0.5, radius - 0.5 do
      strip(x, 0, 1, 2 * math.sqrt(radius * radius - x * x), color)
    end
  end
  disk(25, {0.18, 0.12, 0.07, 1})
  disk(23, {0.78, 0.62, 0.34, 1})
  disk(21, {0.09, 0.25, 0.34, 1})
  disk(19, {0.13, 0.34, 0.43, 1})
  -- Gavel and auction block.
  rectangle(0, 17, 28, 5, {0.22, 0.13, 0.06, 1})
  rectangle(0, 15, 24, 4, {0.78, 0.58, 0.29, 1})
  rectangle(4, 1, 7, 26, {0.23, 0.13, 0.06, 1}, -38)
  rectangle(3, 0, 5, 25, {0.74, 0.46, 0.22, 1}, -38)
  rectangle(-4, -9, 26, 12, {0.23, 0.13, 0.06, 1}, -38)
  rectangle(-4, -10, 24, 10, {0.88, 0.71, 0.40, 1}, -38)
  rectangle(-13.5, -2.5, 5, 13, {0.93, 0.76, 0.43, 1}, -38)
  rectangle(5.5, -17.5, 5, 13, {0.93, 0.76, 0.43, 1}, -38)
  rectangle(-5, -12, 19, 2, {1, 0.91, 0.64, 1}, -38)
end

-- Skin factories can run after login or after recreating the HUD. Keep the
-- original geometry and handlers, and change only the auction button's art.
local originalApplyButtonSkin = ApplyButtonSkin
function ApplyButtonSkin(button, skin, ...)
  local result = originalApplyButtonSkin(button, skin, ...)
  if skin == auctionSkin then drawAuctionIcon(button) end
  return result
end

local originalSetTooltip = SetTooltip
function SetTooltip(text, widget, ...)
  -- Also repair a HUD that was created before a diagnostic driver attached.
  -- UTF-8 bytes for the original Korean label, independent of source encoding.
  if widget and text == '\234\178\189\235\167\164\236\158\165' then
    drawAuctionIcon(widget)
  end
  if widget and widget.aaemuAuctionParts then
    if hoveredButton and hoveredButton ~= widget then tint(hoveredButton, 1) end
    hoveredButton = widget
    tint(widget, 1.12)
    text = 'Auction House\nBuy and sell items with other players.'
  end
  return originalSetTooltip(text, widget, ...)
end

local originalHideTooltip = HideTooltip
function HideTooltip(...)
  if hoveredButton then tint(hoveredButton, 1); hoveredButton = nil end
  return originalHideTooltip(...)
end

AAEmuAuctionUiInstalled = { apply = ApplyButtonSkin, tooltip = SetTooltip, hide = HideTooltip }
return 'ready'
