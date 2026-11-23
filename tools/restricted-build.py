"""Compile with MSBuild's exact compiler arguments when a restricted runner blocks its pipes.

Normal developer builds should use dotnet build. This fallback still runs the compiler,
including analyzers, and only skips MSBuild's redundant compiler launch after it succeeds.
Usage: python tools/restricted-build.py [--configuration Release] [--tests] [--publish]
"""
import argparse
import json
from pathlib import Path
import subprocess


def run(args, cwd, capture=False):
    return subprocess.run(args, cwd=cwd, check=True, text=True, encoding="utf-8",
                          stdout=subprocess.PIPE if capture else None).stdout


def build(project, configuration, publish=False):
    cwd = project.parent
    properties = ["-p:PublishProfile=FolderProfile"] if publish else []
    run(["dotnet", "restore", str(project), "--disable-build-servers", "-v:q", *properties], cwd)
    command = ["dotnet", "msbuild", str(project), "-t:Clean;PrepareResources;Compile",
               "-p:BuildProjectReferences=false", "-p:SkipCompilerExecution=true",
               "-p:ProvideCommandLineArgs=true", f"-p:Configuration={configuration}",
               "-getItem:CscCommandLineArgs", "-getProperty:MSBuildSDKsPath", *properties]
    raw = run(command, cwd, capture=True)
    data = json.loads(raw[raw.index("{"):])
    compiler = Path(data["Properties"]["MSBuildSDKsPath"]).parent / "Roslyn/bincore/csc.dll"
    args = [item["Identity"] for item in data["Items"]["CscCommandLineArgs"]
            if item["Identity"] != "/noconfig"]
    if not args:
        raise RuntimeError("MSBuild returned no compiler arguments")
    rsp = cwd / "obj/restricted-compiler.rsp"
    rsp.write_text("\n".join(args), encoding="utf-8")
    run(["dotnet", str(compiler), "/noconfig", "@" + str(rsp)], cwd)
    run(["dotnet", "publish" if publish else "build", str(project), "--no-restore", "--disable-build-servers",
         "-p:BuildProjectReferences=false", "-p:SkipCompilerExecution=true",
         "-c", configuration, "-v:q", *properties], cwd)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--configuration", choices=["Debug", "Release"], default="Debug")
    parser.add_argument("--tests", action="store_true")
    parser.add_argument("--publish", action="store_true", help="Publish with the existing FolderProfile after validation")
    options = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    build(root / "GhostDeck.csproj", options.configuration)
    if options.tests:
        build(root / "tests/GhostDeck.Tests.csproj", options.configuration)
        run(["dotnet", str(root / "tests/bin" / options.configuration /
                          "net11.0-windows7.0/GhostDeck.Tests.dll")], root)
    if options.publish:
        build(root / "GhostDeck.csproj", options.configuration, publish=True)


if __name__ == "__main__":
    main()
