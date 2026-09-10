# Ookla Speedtest CLI notice

This local build contains the official Ookla Speedtest CLI
`1.2.0.84`. It is not part of Speedtest Dashboard and is not covered by the
dashboard's MIT licence.

Before using it, review Ookla's current terms:

- <https://www.speedtest.net/about/eula>
- <https://www.speedtest.net/about/terms>
- <https://www.speedtest.net/about/privacy>

Public Speedtest Dashboard releases exclude this CLI. Installing it does not
accept its terms. Runtime defaults leave `Providers__Ookla__AcceptLicense`
and `Providers__Ookla__AcceptGdpr` set to `false`; the operator must explicitly
set both to `true` after reviewing and accepting the terms.

Official package source: <https://packagecloud.io/ookla/speedtest-cli>
