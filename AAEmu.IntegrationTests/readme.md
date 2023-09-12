# Setting up Integration Tests

1. Create a copy of ExampleConfig.json and save as Config.json
2. Update this new "Config.json" property to be "copied if newer"
2. Edit the Config.json %wildcards% using the same configurations you did in AAEmu.Game
3. Copy your sqlite database file from AAEmu.Game Data to the IntegrationTests compiled Data folder (bin/Debug.../Data)
4. Clean up the solution
5. All ready to run integration tests