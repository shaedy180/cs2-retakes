import pathlib, shutil, zipfile, hashlib
root = pathlib.Path(__file__).resolve().parents[1]
variants = {}
for suffix, maps in (('', True), ('-no-map-configs', False)):
    output = root / ('output' + suffix)
    plugin = output / 'addons/counterstrikesharp/plugins/RetakesPlugin'
    shared = output / 'addons/counterstrikesharp/shared/RetakesPluginShared'
    for project, destination in (('RetakesPlugin', plugin), ('RetakesPluginShared', shared)):
        destination.mkdir(parents=True, exist_ok=True)
        source = root / project / 'bin/Release/net8.0'
        for extension in ('.dll', '.pdb', '.deps.json'):
            shutil.copy2(source / (project + extension), destination)
    shutil.copytree(root / 'RetakesPlugin/lang', plugin / 'lang', dirs_exist_ok=True)
    if maps:
        shutil.copytree(root / 'RetakesPlugin/map_config', plugin / 'map_config', dirs_exist_ok=True)
    variants['RetakesPlugin' + suffix] = output
for name, source in variants.items():
    archive = root / (name + '.zip')
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
        for path in sorted(source.rglob('*')):
            if path.is_file(): z.write(path, path.relative_to(source))
    archive.with_suffix('.zip.sha256').write_text(hashlib.sha256(archive.read_bytes()).hexdigest() + '  ' + archive.name + '\n')
