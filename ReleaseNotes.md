# 5.18.1.24
* [Dependency change] Uses Fake >= 5.18.1 rather than >= 5.9.3
* [API change] Update to add a Fake `ToolType` to the `Params` object.  If you use the `Create` method, this should be transparent to you, as it defaults to `FullFramework`.
* [API change] In `Params.Create`, use the updated Fake Process APIs to look for Gendarme, which means it will look in PATH first, then locally, and default the `ToolPath` to just plain `gendarme` if nothing is found.

# 5.9.3.10
* [BUGFIX] As "--limit 0" means "report nothing" not "report all", make zero limit emit nothing to the command line
* [Enhancement] `FailBuildOnDefect` parameter, default `true`, to determine if defect detection will fail the build.
* **NOTE** whether --limit is set > 0 or omitted, if there are > 0 defects reported Gendarme exits with return code 1.  It limit is set zero, or there are no defects against the rules engaged, then it returns 0.  Failing the build on >= N defects for N > 0 does not come for free, but would require parsing one form of output or another, and no form of output is guaranteed -- they can all be switched off.

# 5.9.3.7
* [NEW PACKAGE] `AltCode.Fake.DotNet.Gendarme` containing Gendarme task helper types for FAKE scripts (v5.9.3 or later) : see Wiki entry [The `AltCode.Fake.DotNet.Gendarme` package](https://github.com/SteveGilham/altcode.Fake/wiki/The-AltCode.Fake.DotNet.Gendarme-package)