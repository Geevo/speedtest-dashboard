# Interface design

## Audience

The dashboard is for self-hosters and small-network administrators who need to
check public egress identity and connection quality over time.

## Visual system

Use Instrument Sans, neutral black, grey, and off-white surfaces, and one cool
blue accent for focus, selection, links, and charts. Reserve green and red for
status. Support light, dark, and system themes with the same information and
controls at desktop and mobile sizes.

Avoid decorative animation, glass effects, oversized headings, heavy gradients,
and neon-on-dark styling. The result should look like a compact network tool,
not a generic administration template.

## Interaction rules

- Label whether data comes from the backend or the browser.
- Show units, timestamps, errors, and unavailable states explicitly.
- Keep automatic server selection prominent and place advanced controls behind
  an additional action.
- Do not imply that an observed egress address proves VPN connectivity.
- Keep all critical actions available on mobile with touch-sized controls.
