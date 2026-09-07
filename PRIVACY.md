# Privacy

Configured release builds collect feature-usage telemetry by default through Application Insights.
There is no consent prompt or user-facing opt-out.

## Data collected

The only event is `rustanalyzer.feature_usage`.

Event fields:

- `feature` and `action`: `language_server/activate`; `cargo/build`; `cargo/clean`;
  `cargo/clippy`; `cargo/format`; `test_adapter/discover`; `test_adapter/execute`; `launch/debug`;
  `launch/run`; `toolchain/install`; or `toolchain/switch`
- `outcome`: `succeeded`, `failed`, or `cancelled`
- `duration_bucket`: `<1s`, `1–5s`, `5–30s`, `30–120s`, or `≥120s`

Context fields:

- `schema_version`: `1`
- `host_kind`: `vsix` or `test_adapter`
- `extension_version`: `Vsix.Version`
- `visual_studio_major`: `17`, `18`, or `unknown`
- `user.id`

`user.id` is:

```text
ravs-v1: + unpadded Base64URL(
  SHA-256(
    UTF-8(
      Expand("%USERNAME%@%COMPUTERNAME%.%USERDOMAIN%")
    )
  )
)
```

The expanded value is hashed exactly without trimming, normalization, case changes, or salt. If any
placeholder remains unresolved, `user.id` is omitted. This identifier is pseudonymous and can be
guessed from candidate environment values. It is never used for authentication, entitlement,
enforcement, or support identity.

The extension does not send paths, names, commands, arguments, environments, settings, source,
Cargo output, test data, URLs, update metadata, exception details, device context, or arbitrary
properties.

## Operational suppression

`RUSTANALYZER_TELEMETRY_DISABLED` suppresses telemetry when nonempty. It is an operational control
for repository tests, CI, and internal development, not a consent mechanism or documented user
preference. Visual Studio experimental instances and builds without telemetry configuration are
also silent.

## Retention and access

Raw telemetry events are retained for 365 days. Existing Application Insights access controls are
unchanged.
