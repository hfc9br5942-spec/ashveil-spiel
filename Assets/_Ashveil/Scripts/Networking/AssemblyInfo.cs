using System.Runtime.CompilerServices;

// REQ-20261019-003: Testcode liegt in eigenem Assembly und braucht weiter Zugriff auf interne Teile des Netzcodes.
[assembly: InternalsVisibleTo("Ashveil.Networking.Testing")]

[assembly: InternalsVisibleTo("Ashveil.EditorTests")]
