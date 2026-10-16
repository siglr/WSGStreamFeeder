# SimConnect build dependencies

The source repository does not redistribute Microsoft DLLs. Before building on Windows, obtain the x64 SimConnect components from the Microsoft Flight Simulator SDK and copy these two files into this directory:

- `Microsoft.FlightSimulator.SimConnect.dll` — managed wrapper
- `SimConnect.dll` — native x64 library

Use a matching pair compatible with MSFS 2024. Both must remain beside the built application. Do not substitute 32-bit components. The DLLs are ignored by Git and retain Microsoft's original licensing; they are not covered by the project's PolyForm license.

Run `./build.ps1` from the repository root after installing a .NET SDK supporting .NET 8. For normal use, download the complete official application ZIP from GitHub Releases instead of building the source archive.
