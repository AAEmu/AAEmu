_Note: You can find the most recent version of this article at [Manual Installation Guide](https://github.com/AAEmu/AAEmu/wiki/Installation-&-Setup) on our Wiki._

![](https://boards.aaemu.pw/assets/files/2018-10-11/1539288486-150348-aaemu-blank-text.png)

Make sure you checked our [Understanding AAEmu Components](https://github.com/AAEmu/AAEmu/wiki/Components) page to get a better understanding on each of the components you are going to use and install towards this guide.

## Getting Started

This guide will help you get started with the AAEmu project both as an experienced developer or as an enthusiast player wanting to spin up your own private server to play with friends.

## Preparing your environment

### Downloads needed

1. Install MySQL - Archeage State Database

    Download [MySQL 8.0.12 Installer](https://downloads.mysql.com/archives/get/p/25/file/mysql-installer-community-8.0.12.0.msi) and follow all the default wizard setup instructions to install your mysql server

1. Install .NET SDK.

    Download [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) and follow all the default wizard setup instructions to install the SDK, this is required to build and run the project.

1. Download - AAEmu Repository
   
   Go to [AAEmu Repository](https://github.com/AAEmu/AAEmu) and download the repository.
   
   We strongly recommend to use the `develop` branch (stable).
   
   You can download the repository as a `zip` file or `clone` it using git.

   ![image](https://github.com/AAEmu/AAEmu/assets/19890735/d8c2b092-ea7f-4c53-b1fa-9ad1033d241d)

1. Download - Archeage Reference Sqlite Database

    Download the server `compact.sqlite3` from [Google Drive](https://drive.google.com/file/d/18Nm_Q7OgWOfdw_8Xl4TBXa1Z51uGHEIh/view) or [MEGA](https://mega.nz/file/ujhFAaIS#disveSrjdUVjY9mZ3Q2xJ2b7I4te2gwbKnzMYD8HLZ4) and copy this file to the `AAEmu.Game/Data` folder in the location where you downloaded from the repository (previous step).

1. Download and Extract - Archeage Client

    Download the Archeage Client v1.2 (Trion_1.2 r208022) from one of the options below:

    - [Option 1 (Mega)](https://mega.nz/folder/C3Q0WQjT#vRUethZLPiYSo2B4nE_etg/file/qyAVQY4I)
    - [Option 2 (Mega)](https://mega.nz/folder/GnwjQCrZ#WNWzX_lDvkzCqoTtt7I42Q)
    - [Option 3 (Google Drive)](https://drive.google.com/drive/folders/1_pIBVHIm1YFal-nteGaVuXjTv3Yrsv4Q)

1. Download and Extract - Archeage Game Launcher

    [Download Latest](https://github.com/ZeromusXYZ/AAEmu-Launcher/releases/latest)

### Setup

#### Setup MySQL - Archeage State Database

1. Open MySQL Workbench (Which should have been included in the MySQL setup above) and create two schemas for AAEmu to use.

   ![image](https://github.com/AAEmu/AAEmu/assets/19890735/9399c1aa-5a7b-4a5a-9e0c-b1d230b5842c)

1. Name these schemas `aaemu_game` and `aaemu_login`, your workbench should now look like this:

   ![image](https://github.com/AAEmu/AAEmu/assets/19890735/7529c094-8aa2-4d81-98f1-4f6280fea5ab)

1. After you have made both schemas, select the **aaemu_login** schema by double clicking it.
   
   You should see it become **bold** (Like the aaemu_game schema is the picture above) to indicate that it is selected.

1. Go to the location where you downloaded from the repository and enter into the `SQL` folder
   
   ![image](https://github.com/AAEmu/AAEmu/assets/19890735/d9a79210-e391-4e20-8514-910a5107597f)

1. Drag **aaemu_login.sql** file into your MySQL workbench

   ![image](https://github.com/AAEmu/AAEmu/assets/19890735/0612393b-fa52-433f-80af-635b1fadbba1)


1. Click the lightning bolt icon over the text to run the commands.
   
   ![image](https://github.com/AAEmu/AAEmu/assets/19890735/a8a6a9db-9a6f-429e-b1c3-b0b2d4bd99d2)

1. Select **aaemu_game** schema and repeat the process for **aaemu_game.sql** file.

1. After you have generated your tables in this way, select the **aaemu_login** schema and open an sql tab if one isn’t already open using this icon.
   
   ![image](https://github.com/AAEmu/AAEmu/assets/19890735/67c15d05-bc7f-4363-80b9-c43401bf4a6c)

1. This first command will add a game server into the database, named AAEmu.Game, running on your local IP on port 1239

    Enter the following command into the tab and execute it.

    ```sql
    INSERT INTO `game_servers` (`id`, `name`, `host`, `port`, `hidden`) VALUES ('1', 'AAEmu.Game', '127.0.0.1', '1239', '0');
    ```

1. _(optional)_ The second will create a login for you to use with the username and password as `test`.
   By default we have auto-create accounts enabled, so this step is only needed if you want to disable it.
   
   Enter the following command into the tab and execute it.

    ```sql
    INSERT INTO `users` (`id`, `username`, `password`, `email`, `last_login`, `last_ip`, `created_at`, `updated_at`) VALUES (NULL, 'test', 'n4bQgYhMfWWaL+qgxVrQFaO/TxsrC4Is0V1sFbDwCgg=', '', '0', '', '0', '0');
    ```

1. Well done, you have now setup your MySQL database for AAEmu.

#### Game Server Configuration

1. **Build the project**. Open the command prompt in the location where you downloaded from the repository and run the following command:

    ```powershell
    dotnet build
    ```

    Result should be like below:
    
    ![image](https://github.com/AAEmu/AAEmu/assets/19890735/3cae3135-b365-48ac-89d3-4d6ee6efd0b0)

1. Go to the location where you downloaded from the repository and enter into the `AAEmu.Game\bin\Debug\net8.0` folder

1. Find the `exampleconfig.json` file, rename it to `Config.json` and update its contents

    Change the values to fit your system. You can find details on this [here](https://github.com/AAEmu/AAEmu/wiki/Working-with-the-Config.json-files-and-server-listings#game-server-configjson)

    The file contents show look similar to the below. **Change the user and password info to what you setup in your MySQL installation.**

    ```json
    {
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
        "LoginNetwork": {
            "Host": "127.0.0.1",
            "Port": "1234"
        },
        "WebApiNetwork": {
            "Host": "*",
            "Port": 1280
        },
        "Connections": {
            "MySQLProvider": {
                "Host": "localhost",
                "Port": "3306",
                "User": "change to your user name",
                "Password": "change to your user password",
                "Database": "aaemu_game"
            }
        },
        "CharacterNameRegex": "^[a-zA-Z0-9а-яА-Я]{1,18}$",
        "MaxConcurencyThreadPool": 8,
        "HeightMapsEnable": true
    }
    ```

1. Find the file `AAEmu.Game\bin\Debug\net8.0\Configurations\ClientData.json` and open it

1. Locate the **root folder path** where you extracted the Archeage Client, copy the full path to the `game_pak` file (should be the biggest file in the folder)

   ![image](https://github.com/AAEmu/AAEmu/assets/19890735/8057a89d-d773-4294-a5ed-1c22dfaf69ef)

1. Add the path as one of the `Source` options in the `ClientData.json` file like the following:
    ```
    {
        "ClientData": {
            "PreferClientHeightMap": true,
            "Sources": [
                "ClientData",
                "ClientData/game_pak",
                "C:/path/to/your/archeage/client/game_pak"
            ]
        }
    }
    ```
    Note the path here uses forward slashes, and the last entry does not require a comma at the end.

#### Login Server Configuration

1. Go to the location where you downloaded from the repository and enter into the `AAEmu.Login\bin\Debug\net8.0` folder

1. Find the `exampleconfig.json` file, rename it to `config.json` and update its contents

    Change the values to fit your system. You can find details on this [here](https://github.com/AAEmu/AAEmu/wiki/Working-with-the-Config.json-files-and-server-listings#login-server-configjson)

    The file contents show look similar to the below. **Change the user and password info to what you setup in your MySQL installation.**

    ```json
    {
        "SecretKey": "test",
        "AutoAccount": true,
        "InternalNetwork": {
            "Host": "127.0.0.1",
            "Port": 1234
        },
        "Network": {
            "Host": "*",
            "Port": 1237,
            "NumConnections": 10
        },
        "Connections": {
            "MySQLProvider": {
                "Host": "127.0.0.1",
                "Port": "3306",
                "User": "change to your user name",
                "Password": "change to your user password",
                "Database": "aaemu_login"
            }
        }
    }
    ```

#### Launcher Configuration

1. Go the folder where you extracted the Launcher and open it.

   ![image](https://github.com/AAEmu/AAEmu/assets/19890735/4b428cf6-d342-4e06-bb09-47f62d547117)

1. Click in the `Path to Game` input, locate your Archeage Client folder within the `bin32` folder and select the `archeage.exe` file like the following:
 
   ![image](https://github.com/AAEmu/AAEmu/assets/19890735/b04ec3c9-9d30-44cc-ad0c-69c7837c5a63)

#### Running the servers

Start the servers in the following order:

1. Go to the location where you downloaded from the repository and enter into the `Scripts` folder

1. Run the `StartLoginServer.bat`

1. Run the `StartGameServer.bat`

1. After a few moments you should see similar outputs in the command prompt windows:

   ![image](https://github.com/AAEmu/AAEmu/assets/19890735/bc6752b9-df83-45c4-8e71-4a696b1b88c3)

#### Playing the game

1. Open the Launcher and configure your username and password
   
   If you installed the optional SQL command earlier, you can use `test` on both the username and password here.

   Otherwise if auto-create is enabled (default), you can use your own username and password you want. Just make sure your username is not too long (32 characters maximum). **For your own safety** do not use the same password that you use on any other (online) account as this password will be stored in plain text during authentication.
   
   **You can also change or add login information in the MySQL `aaemu_login.users` table if you want auto-account-creation to be disabled**
   
   ![image](https://github.com/AAEmu/AAEmu/assets/19890735/d3ab9caf-6452-4e2d-8b7b-297519485788)

1. Click in the `Play` button and you should see the ArcheAge Client starting.

__Happy playing! 🥳🥳🥳__
