"""Add the just-built release zip to manifest.json (newest version first)."""
import datetime, hashlib, json, os, subprocess

version, zip_name = os.environ["VERSION"], os.environ["ZIP"]
repo, tag = os.environ["GITHUB_REPOSITORY"], os.environ["GITHUB_REF_NAME"]
notes = subprocess.run(["git", "tag", "-l", "--format=%(contents:subject)", tag],
                       capture_output=True, text=True).stdout.strip()

with open("manifest.json") as f:
    manifest = json.load(f)
versions = [v for v in manifest[0]["versions"] if v["version"] != version]
versions.insert(0, {
    "version": version,
    "changelog": notes or tag,
    "targetAbi": "12.4.0.0",
    "sourceUrl": f"https://github.com/{repo}/releases/download/{tag}/{zip_name}",
    "checksum": hashlib.md5(open(zip_name, "rb").read()).hexdigest(),
    "timestamp": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
})
manifest[0]["versions"] = versions
with open("manifest.json", "w") as f:
    json.dump(manifest, f, indent=2)
    f.write("\n")
