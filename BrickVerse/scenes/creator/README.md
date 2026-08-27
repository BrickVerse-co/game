# Creator UI convention

Creator interfaces must be authored as reusable `.tscn` scenes. C# scripts should bind scene nodes and implement behavior, data loading, and dynamic result rows; they should not construct complete window, toolbar, form, or panel layouts in code.

Small runtime-generated controls are acceptable only when their quantity or schema comes from live data. Shared visual structures belong under `scenes/creator/components`, and complete tools belong under `scenes/creator/popups`, `docks`, or `tabs`.
