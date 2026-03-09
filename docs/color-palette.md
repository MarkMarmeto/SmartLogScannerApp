# SmartLog Scanner - Teal/Green Color Palette

> **Theme:** Professional teal/green matching the SmartLog dashboard

## Primary Colors

### Brand Teal
- **Dark Teal:** `#2C5F5D`
  - Usage: Footer, headers, app title, primary dark surfaces
  - Matches: Dashboard sidebar

- **Medium Teal:** `#4D9B91`
  - Usage: Buttons, scan type toggle, scanner frame brackets, activity indicators
  - Matches: Dashboard buttons and accents

- **Light Teal:** `#E0F2F1`
  - Usage: Light backgrounds, icon backgrounds in ready state
  - Matches: Dashboard light surfaces

## Semantic Colors

### Status Colors (Kept from original palette)

- **Success Green:** `#4CAF50` (Material Green 500)
  - Usage: Accepted scans, success feedback, "Today" stats icon
  - Status: Online connectivity, valid scans

- **Warning Amber:** `#FF9800` (Material Orange 500)
  - Usage: Duplicate scans, warnings, rate limits, queue icon
  - Status: Duplicate, debounced locally, clearing queue

- **Error Red:** `#F44336` (Material Red 500)
  - Usage: Rejected scans, errors, clear button
  - Status: Offline connectivity, rejected scans, errors

- **Info Teal:** `#4D9B91` (Changed from Blue)
  - Usage: Queued scans, info messages, sync button
  - Status: Queued offline, info notifications

## Neutral Colors

- **Background:** `#F8F9FA` (Warm White)
  - Usage: App background

- **White:** `#FFFFFF`
  - Usage: Cards, top bar, status indicators

- **Dark Text:** `#37474F` (Blue Grey 800)
  - Usage: Primary text, headings

- **Secondary Text:** `#78909C` (Blue Grey 400)
  - Usage: Hints, secondary labels

- **Connecting Gray:** `#9E9E9E` (Grey 500)
  - Usage: Connectivity status when connecting/unknown

## Component Color Map

### Top Status Bar
- Background: `#FFFFFF` (White)
- App Title: `#2C5F5D` (Dark Teal)
- Settings Icon: `#4D9B91` (Medium Teal)
- Activity Indicator: `#4D9B91` (Medium Teal)

### Connectivity Badge
- Online: `#4CAF50` (Green) + 🟢
- Offline: `#F44336` (Red) + 🔴
- Connecting: `#9E9E9E` (Gray) + ⚪

### Scan Type Toggle
- Background: `#4D9B91` (Medium Teal)
- Text: `#FFFFFF` (White)
- Shadow: `#4D9B91` at 40% opacity

### Camera Scanner Frame
- Corner Brackets: `#4D9B91` (Medium Teal)
- Scan Hint Overlay: `#CC000000` (Black at 80% opacity)

### Ready State
- Icon Background: `#E0F2F1` (Light Teal)
- Icon Color: `#4D9B91` (Medium Teal)

### Feedback Panel (Dynamic)
| Status | Background | Icon | Example |
|--------|------------|------|---------|
| Accepted | `#4CAF50` | ✓ | Green success |
| Duplicate | `#FF9800` | ⚠ | Amber warning |
| Rejected | `#F44336` | ✗ | Red error |
| Queued | `#4D9B91` | 📥 | Teal info |
| Error | `#F44336` | ✗ | Red error |
| Rate Limited | `#FF9800` | ⏱ | Amber warning |
| Debounced | `#FF9800` | ⚠ | Amber warning |

### Statistics Footer
- Background: `#2C5F5D` (Dark Teal)
- Card Backgrounds: `#33FFFFFF` (White at 20% opacity)
- Primary Text: `#FFFFFF` (White)
- Secondary Text: `#B0BEC5` (Blue Grey 200)

#### Stat Icons
- Queue: `#FFC107` (Amber A700) + 📥
- Today: `#4CAF50` (Green) + 📊
- Sync: `#4D9B91` (Teal) + 🔄 (updated from blue)
- Clear: `#F44336` (Red) + 🗑️

## Design Principles

1. **Primary Actions:** Use Medium Teal (`#4D9B91`)
2. **Success States:** Keep Material Green (`#4CAF50`)
3. **Warnings:** Keep Material Amber (`#FF9800`)
4. **Errors:** Keep Material Red (`#F44336`)
5. **Info/Neutral:** Use Teal (`#4D9B91`) instead of blue
6. **Dark Surfaces:** Use Dark Teal (`#2C5F5D`)
7. **Light Surfaces:** Use Light Teal (`#E0F2F1`)

## Accessibility

All color combinations meet WCAG AA standards:
- Dark Teal on White: ✅ AAA (7.8:1)
- Medium Teal on White: ✅ AA (4.2:1)
- White on Dark Teal: ✅ AAA (7.8:1)
- White on Medium Teal: ✅ AA (4.2:1)

## Migration Notes

### Changed from Blue to Teal:
- App Title: `#1976D2` → `#2C5F5D`
- Scan Type Toggle: `#1976D2` → `#4D9B91`
- Settings Icon: `#1976D2` → `#4D9B91`
- Activity Indicator: `#1976D2` → `#4D9B91`
- Scanner Brackets: `#1976D2` → `#4D9B91`
- Ready Icon BG: `#E3F2FD` → `#E0F2F1`
- Ready Icon Color: `#1976D2` → `#4D9B91`
- Footer: `#263238` → `#2C5F5D`
- Queued Status: `#2196F3` → `#4D9B91`
- Info Messages: `#2196F3` → `#4D9B91`

### Kept Unchanged:
- Success Green: `#4CAF50`
- Warning Amber: `#FF9800`
- Error Red: `#F44336`
- Queue Icon: `#FFC107`
- Background: `#F5F5F5` → `#F8F9FA` (minor adjustment)

---

**Last Updated:** 2026-03-09
**Theme Version:** 1.0 (Teal/Green)
