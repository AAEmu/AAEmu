-- Run in a diagnostic client's UI state after server select is visible:
-- Get-Content auction_ui_tests.lua -Raw | aaemu-clientdriver lua --pid PID -t ui -
-- Uses native widgets, without entering the world or sending auction requests.
assert(type(AAEmuAuctionUiInstalled) == 'table', 'auction UI patch was lost during login')
local frame = CreateEmptyWindow('AAEmuAuctionVerification', 'UIParent')
frame:SetExtent(150, 57)
frame:AddAnchor('BOTTOMRIGHT', 'UIParent', -50, -60)
frame:Show(true)

local button = frame:CreateChildWidget('button', 'auction', 0, true)
button:AddAnchor('CENTER', frame, -40, 0)
button:SetHandler('OnClick', function() end)
ApplyButtonSkin(button, BUTTON_HUD.TOGGLE_AUCTION)
assert(button:GetWidth() == 59 and button:GetHeight() == 57, 'auction geometry changed')
assert(button.aaemuAuctionParts and #button.aaemuAuctionParts > 0, 'auction artwork missing')
assert(button:HasHandler('OnClick'), 'auction click handler was removed')
local count = #button.aaemuAuctionParts
ApplyButtonSkin(button, BUTTON_HUD.TOGGLE_AUCTION)
assert(#button.aaemuAuctionParts == count, 'reapplying a skin duplicated artwork')

local other = frame:CreateChildWidget('button', 'other', 1, true)
other:AddAnchor('CENTER', frame, 30, 0)
ApplyButtonSkin(other, BUTTON_HUD.MAIN_MENU_MAP)
assert(other.aaemuAuctionParts == nil, 'a different HUD button was replaced')
assert(other:GetWidth() == 32 and other:GetHeight() == 32, 'other button geometry changed')

-- A separate skin table models a button created before the driver attached.
local late = frame:CreateChildWidget('button', 'late', 2, true)
late:AddAnchor('CENTER', frame, -105, 0)
local copiedSkin = {}
for k, v in pairs(BUTTON_HUD.TOGGLE_AUCTION) do copiedSkin[k] = v end
ApplyButtonSkin(late, copiedSkin)
assert(late.aaemuAuctionParts == nil, 'unrelated skin table matched the factory')

-- Observe the actual text forwarded to the original native tooltip helper.
local tooltip = SetTooltip
local slot, original
for i = 1, 20 do
  local name, value = debug.getupvalue(tooltip, i)
  if not name then break end
  if name == 'originalSetTooltip' then slot, original = i, value; break end
end
assert(slot, 'native tooltip forwarding helper missing')
local forwarded
debug.setupvalue(tooltip, slot, function(text, widget, ...)
  forwarded = text
  return original(text, widget, ...)
end)
local ok, message = pcall(function()
  SetTooltip('\234\178\189\235\167\164\236\158\165', late, false, false)
  assert(late.aaemuAuctionParts, 'late attachment did not repair the icon')
  assert(forwarded == 'Auction House\nBuy and sell items with other players.', 'Korean label was not translated')
  SetTooltip('Map control', other, false, false)
  assert(forwarded == 'Map control', 'another tooltip was changed')
end)
debug.setupvalue(tooltip, slot, original)
assert(ok, message)
late:Show(false)
other:Show(false)
HideTooltip()
SetTooltip('', button, false, false)
AAEmuAuctionVerification = frame
return 'PASS: login persistence, artwork, geometry, click handler, repeated skinning, other controls, late attachment and translation'
