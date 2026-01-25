## Setup instructions

### Configuration

To configure the game server you have you can just use a **configuration file** or combine it with **user secrets (preferred)** to hide credentials from files in the repository.

The configuration structure is as follows:

```
"Id": 1,
"AdditionalesId": [],
"SecretKey": "test",
"Network": {
    "Host": "*",
    "Port": 1239,
    "NumConnections": 10
},
"StreamNetwork": {
    "Host": "*",
    "Port": 1250
},
"WebApiNetwork": {
    "Host": "*",
    "Port": 1280
},
"LoginNetwork": {
    "Host": "%login_host%",             <-- the IP address of the login server
    "Port": "%login_port%"              <-- the port of the login server
},
"Connections": {
    "MySQLProvider": {
        "Host": "%db_host%",            <-- localhost or any specific
        "Port": "%db_port%",            <-- 3306 or any specific
        "User": "%db_user%",            <-- root or any specific
        "Password": "%db_password%",    <-- password
        "Database": "aaemu_game"
    }
},
"CharacterNameRegex": "^[a-zA-Z0-9а-яА-Я]{1,18}$",
"MaxConcurencyThreadPool": 8,
"HeightMapsEnable": false
```

### Create Configuration File

1. Create a file named `Config.Local.json` next to `Config.json` in the `AAEmu.Game` directory.
   This file will override the default `Config.json` file with your local changes.
1. Open `Config.Local.json` and add the configuration details as required.
   **Don't provide any credentials in this file if you want to use User Secrets (see below)**

For example, the `Config.Local.json` file could look like this:

```
{
    "Connections": {
        "MySQLProvider": {
            "Host": "localhost",
            "Port": "3306",
            "User": "root",
            "Password": "MySuperSecurePassword",
        }
    }
}
```

### Combining with User Secrets (preferred)

This is the preferred option as it won't expose your database credentials in the configuration file.

1. Open a command prompt in the `AAEmu.Game` directory
1. Start a user secrets session by running `dotnet user-secrets init`
1. Set the required secrets by running:

    ```
    dotnet user-secrets set "Connections:MySQLProvider:User" "your username"
    dotnet user-secrets set "Connections:MySQLProvider:Password" "your password"

    + any other configuration details you want change
    ```

1. Check the secrets have been set by running `dotnet user-secrets list`
   Result will be like below **but with your values**:

    ```
    Connections:MySQLProvider:User = root
    Connections:MySQLProvider:Password = yourpassword
    ```
