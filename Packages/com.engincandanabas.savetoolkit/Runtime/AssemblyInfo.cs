
using System.Runtime.CompilerServices;
using UnityEngine.Scripting;

// Keep the whole assembly under IL2CPP managed code stripping: serializers
// and storages are created through reflection and interfaces.
[assembly: AlwaysLinkAssembly]
[assembly: Preserve]

[assembly: InternalsVisibleTo("SaveToolkit.Editor")]
[assembly: InternalsVisibleTo("SaveToolkit.Tests.Editor")]
