# User Personas

**Version:** 1.0.0
**Last Updated:** 2026-02-13
**Status:** Validated

Personas for SmartLog Scanner. Referenced in user stories to ensure features are designed with specific users in mind.

---

## Guard Gary

**Role:** School Security Guard
**Technical Proficiency:** Novice
**Primary Goal:** Scan student QR codes quickly and reliably during peak gate hours without delays or confusion.

### Background
Gary is a security guard stationed at a school gate. He processes hundreds of students during morning arrival and afternoon departure. He has basic familiarity with touchscreens and phones but is not comfortable troubleshooting technology. He works outdoors or in a guard booth, sometimes in bright sunlight. Speed and clarity are everything during the 30-minute peak windows.

### Needs & Motivations
- Instant, unambiguous visual feedback — green means good, red means problem
- Audio confirmation so he can glance away while students pass
- Zero decision-making during scanning — point, scan, see result, next student
- Confidence that scans are not lost if the network drops
- Simple ENTRY/EXIT toggle he can switch once at the start of a shift

### Pain Points
- Small text or subtle UI differences he can't read quickly
- Error messages with technical jargon he doesn't understand
- Having to restart or reconfigure the app during peak hours
- Not knowing if the system is actually working when it's offline
- Multiple steps or confirmations before a scan is processed

### Typical Tasks
- Scan student QR codes as they enter or exit the school gate
- Glance at the result to confirm the student is accepted
- Toggle between ENTRY and EXIT mode at shift changeover
- Notice if the system goes offline (status indicator)
- Call IT if something is wrong (but cannot troubleshoot himself)

### Quote
> "I just need it to beep green or beep red. That's it. Don't make me think."

---

## IT Admin Ian

**Role:** School IT Administrator
**Technical Proficiency:** Intermediate
**Primary Goal:** Deploy, configure, and maintain scanner devices across school gates with minimal ongoing support burden.

### Background
Ian manages the school's IT infrastructure, including the SmartLog Admin Web App server and all gate scanner devices. He registers devices in the admin panel, generates API keys, and installs the SmartLog Scanner app on gate PCs and Macs. He handles 3-8 scanner devices across the school campus. He is comfortable with network configuration, IP addresses, and basic troubleshooting but is not a developer.

### Needs & Motivations
- One-time setup that rarely needs revisiting
- Clear error messages during setup that tell him exactly what's wrong (wrong URL, bad API key, server unreachable)
- Confidence that the scanner will auto-recover from network outages
- Ability to verify that a device is properly connected during setup
- Knowing that queued scans will sync automatically without his intervention

### Pain Points
- Getting called to the gate during peak hours because the scanner "isn't working"
- Vague connection errors that don't distinguish between bad URL, bad key, and server down
- Having to physically visit a gate machine to reconfigure after a server change
- Guards calling about issues that would resolve themselves (temporary offline)
- Devices that don't recover gracefully after crashes or power outages

### Typical Tasks
- Install SmartLog Scanner on a new gate machine (Windows or Mac)
- Run the first-launch setup wizard: enter server URL, API key, HMAC secret
- Test the connection to verify the device is properly registered
- Choose between camera and USB scanner mode based on the hardware at each gate
- Troubleshoot connectivity issues by checking the status indicator or logs

### Quote
> "Set it up once, and it should just work. If something goes wrong, I need to know exactly what, not just 'connection failed'."

---

## Persona Usage Guide

| Persona | Used For Stories About |
|---------|----------------------|
| Guard Gary | Scanning, feedback display, audio, toggle, offline experience, main screen UX |
| IT Admin Ian | Setup wizard, configuration, connection testing, error messaging, deployment |

---

## Changelog

| Date | Version | Changes |
|------|---------|---------|
| 2026-02-13 | 1.0.0 | Initial personas — 2 personas created (Guard Gary, IT Admin Ian) |
