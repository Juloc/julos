# JulOS Adaptive Browser

This package is retained as a legacy experimental implementation and runtime reference.

It is **not** a separate user-facing Browser product anymore. JulOS exposes one Browser whose normal
mode is the transparent JulOS proxy. The useful server-streaming work from Adaptive Browser may be
reused later for the explicit Remote mode of that same Browser.

Do not add an `Adaptive Browser` launcher entry, separate tabs, separate user preferences or a
second browser workspace. New Browser product work belongs to the unified Browser contract in
`docs/WEB-APP-RENDERING.md` and decision D042.

The existing runtime/provider implementation may remain buildable while migration work is pending.
