# Design guidelines

### Users

Self-hosters, homelab operators, and small-network administrators. They need to confirm the backend's public egress identity, compare connection quality over time, choose public speed-test servers, and schedule repeatable checks.

### Brand Personality

Dependable, restrained, and exact. The interface should create confidence that measurements are honest and clearly identify whether information comes from the backend, while avoiding claims that an IP address alone proves VPN state.

### Aesthetic Direction

A high-contrast network operations console with neutral black, grey, and off-white surfaces. Support light, dark, and system themes. Use a single cool blue accent for focus, selection, links, and charts; reserve semantic green and red for status only. Information-dense without becoming a generic CRUD admin panel. Avoid warm page tints, excessive gradients, glassmorphism, oversized hero treatments, neon-on-dark styling, and decorative animation. Support desktop and mobile equally.

### Design Principles

1. Make origin and status explicit: always distinguish backend-container measurements from browser state.
2. Prefer operational clarity over decoration: labels, units, timestamps, errors, and unavailable states must be unambiguous.
3. Use progressive disclosure: keep the common automatic-server path obvious and reveal advanced controls when needed.
4. Preserve trust: never fabricate provider progress or imply that an egress IP proves VPN connectivity.
5. Adapt rather than amputate: mobile layouts retain all critical actions with touch-friendly controls.
