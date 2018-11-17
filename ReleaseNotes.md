# 5.9.3.xxx
* [BUGFIX] As "--limit 0" means "report nothing" not "report all", make zero limit emit nothing to the command line
* [Enhancement] `FailBuildOnDefect` parameter, default `true`, to determine if defect detection will fail the build

# 5.9.3.7
* [NEW PACKAGE] `AltCode.Fake.DotNet.Gendarme` containing Gendarme task helper types for FAKE scripts (v5.9.3 or later) : see Wiki entry [The `AltCode.Fake.DotNet.Gendarme` package](https://github.com/SteveGilham/altcode.Fake/wiki/The-AltCode.Fake.DotNet.Gendarme-package)