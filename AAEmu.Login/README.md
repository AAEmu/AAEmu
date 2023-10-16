## Setup instructions

### Configuration

To configure the login you have you can just use a **configuration file** or combine it with **user secrets (preferred)** to hide credentials from files in the repository.

The configuration structure is as follows:

```
"SecretKey": "test",
"AutoAccount": true,
"InternalNetwork": {  <-- Internal network (for game server)
    "Host": "*",
    "Port": 1234
},
"Network": {          <-- External network (for clients)
    "Host": "*",
    "Port": 1237,
    "NumConnections": 10
},
"Connections": {
    "MySQLProvider": {
        "Host": "%db_host%",         <-- localhost or any specific
        "Port": "%db_port%",         <-- 3306 or any specific
        "User": "%db_user%",         <-- root or any specific
        "Password": "%db_password%", <-- password
        "Database": "aaemu_login"
    }
}

```

### Copy Configuration File

1. Copy `ExampleConfig.json` as `Config.json` in the `AAEmu.Login` directory
1. Open `Config.json` and change the configuration details as required
   **Don't provide any credentials in this file if you want to use User Secrets (see below)**

### Combining with User Secrets (preferred)

This is the preferred option as it won't expose your database credentials in the configuration file.

1. Open a command prompt in the `AAEmu.Login` directory
1. Start a user secrets session by running `dotnet user-secrets init`
1. Set the required secrets by running:

    ```
    dotnet user-secrets set "Connections:MySQLProvider:User" "your username"
    dotnet user-secrets set "Connections:MySQLProvider:Port" "port number"
    dotnet user-secrets set "Connections:MySQLProvider:Password" "your password"
    dotnet user-secrets set "Connections:MySQLProvider:Host" "localhost or specific ip"

    + any other configuration details you want change
    ```

1. Check the secrets have been set by running `dotnet user-secrets list`
   Result will be like below **but with your values**:

    ```
    Connections:MySQLProvider:User = root
    Connections:MySQLProvider:Port = 3306
    Connections:MySQLProvider:Password = yourpassword
    Connections:MySQLProvider:Host = localhost
    ```
