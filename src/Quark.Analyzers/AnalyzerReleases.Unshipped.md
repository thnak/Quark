; Unshipped analyzer releases

### New Rules

 Rule ID | Category  | Severity | Notes
---------|-----------|----------|--------------------------------
 QRK0001 | Quark.AOT           | Error   | Avoid dynamic type resolution
 QRK0002 | Quark.AOT           | Error   | Avoid dynamic assembly loading
 QRK0003 | Quark.AOT           | Error   | Do not use ISerializable
 QRK0004 | Quark.AOT           | Warning | Instance GetType() used to drive runtime dispatch
 QRK0010 | Quark.DataIsolation | Info    | Grain argument will be shallow-cloned
 QRK0011 | Quark.DataIsolation | Warning | Grain return type will not be deep-copied
 QRK0012 | Quark.DataIsolation    | Info    | Grain argument uses boxed fallback serialization
 QRK0020 | Quark.BehaviorLifecycle | Warning | Mutable instance field on grain behavior will be reset between calls
 QRK0021 | Quark.BehaviorLifecycle | Warning | Writable auto-property on grain behavior will be reset between calls
 QRK0022 | Quark.BehaviorLifecycle | Warning | Static mutable state on grain behavior is shared across all activations
 QRK0030 | Quark.Performance    | Warning | Grain behavior method returns Task; consider ValueTask
 QRK0031 | Quark.Performance    | Info    | Use ValueTask-native completion instead of Task.CompletedTask/Task.FromResult
 QRK0040 | Quark.Reentrancy     | Warning | Non-reentrant grain behavior awaits a call back into its own grain interface
