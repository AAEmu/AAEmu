# ![AAEmu](https://i.imgur.com/NFDY376.png)

[![Coverage Status](https://coveralls.io/repos/github/AAEmu/AAEmu/badge.svg?branch=develop)](https://coveralls.io/github/AAEmu/AAEmu?branch=develop)
![Discord](https://img.shields.io/discord/479677351618281472?color=%235865F2&label=Discord&logo=Discord&logoColor=%23FFFFFF")

![](https://boards.aaemu.pw/assets/files/2018-10-11/1539288486-150348-aaemu-blank-text.png)

Make sure you checked our [Understanding AAEmu Components](https://github.com/NL0bP/AAEmu/wiki/Components) page to get a better understanding on each of the components you are going to use and install towards this guide.

## Getting Started

This guide will help you get started with the AAEmu project both as an experienced developer or as an enthusiast player wanting to spin up your own private server to play with friends.

## Preparing your environment

### Downloads needed

1.  Install MySQL - Archeage State Database

    Download [MySQL 8.0.32 Installer](https://downloads.mysql.com/archives/get/p/25/file/mysql-installer-community-8.0.32.0.msi) and follow all the default wizard setup instructions to install your mysql server

2.  Install .NET SDK.

    Download [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) and follow all the default wizard setup instructions to install the SDK, this is required to build and run the project.

3.  Download - AAEmu Repository

    Go to [AAEmu Repository](https://github.com/NL0bP/AAEmu) and download the repository [AAEmu 3.0.3.0](https://github.com/NL0bP/AAEmu/tree/client_version/3.0_client_\(2017-03-15\)%2B).

    We strongly recommend to use the `client_version/3.0_client(2017-03-15)+` branch.

    You can download the repository as a `zip` file or `clone` it using git.

    ![image](https://github.com/AAEmu/AAEmu/assets/19890735/d8c2b092-ea7f-4c53-b1fa-9ad1033d241d)

4.  Download - Archeage Reference Sqlite Database

    [Attention! The format of the database has been changed:](https://github.com/NL0bP/AAEmu/commit/f02a1b42d6c87629dce7a08b34cd3da5db82802b)

    *   Now requires the database compact.server.table.sqlite3 - contains server tables;
    *   Now the database required is compact.sqlite3 - the database from the current client;
    *   Find the link to download the database in our discord, in the FAQ section;

    Download \[compact.sqlite3 & compact.server.table.sqlite3] and copy this files to the `AAEmu.Game/Data` folder in the location where you downloaded from the repository (previous step).

5.  Download and Extract - Archeage Client

    Download the Archeage Client v.3.0.3.0 (r330995) from one of the options below:

    *   [Option 1 (Mega)](https://mega.nz/folder/C3Q0WQjT#vRUethZLPiYSo2B4nE_etg/file/urYCTQ4a)
    *   [Option 2 (GDrive)](https://drive.google.com/file/d/1KQE-OIgGaOSqr69MufLe8R6odaIK8nit/view?usp=sharing)

6.  Download and Extract - Archeage Game Launcher

    [Download Latest](https://github.com/ZeromusXYZ/AAEmu-Launcher/releases/latest)

### Setup

#### Setup MySQL - Archeage State Database

1.  Open MySQL Workbench (Which should have been included in the MySQL setup above) and create two schemas for AAEmu to use.

    ![image](https://github.com/AAEmu/AAEmu/assets/19890735/9399c1aa-5a7b-4a5a-9e0c-b1d230b5842c)

2.  Name these schemas `aaemu_game_3030` and `aaemu_login`, your workbench should now look like this:

    ![image](https://github.com/AAEmu/AAEmu/assets/19890735/7529c094-8aa2-4d81-98f1-4f6280fea5ab)

3.  After you have made both schemas, select the **aaemu\_login** schema by double clicking it.

    You should see it become **bold** (Like the aaemu\_game\_3030 schema is the picture above) to indicate that it is selected.

4.  Go to the location where you downloaded from the repository and enter into the `SQL` folder

    ![image](https://github.com/AAEmu/AAEmu/assets/19890735/d9a79210-e391-4e20-8514-910a5107597f)

5.  Drag **aaemu\_login.sql** file into your MySQL workbench

    ![image](https://github.com/AAEmu/AAEmu/assets/19890735/0612393b-fa52-433f-80af-635b1fadbba1)

6.  Click the lightning bolt icon over the text to run the commands.

    ![image](https://github.com/AAEmu/AAEmu/assets/19890735/a8a6a9db-9a6f-429e-b1c3-b0b2d4bd99d2)

7.  Select **aaemu\_game\_3030** schema and repeat the process for **aaemu\_game.sql** file.

8.  After you have generated your tables in this way, select the **aaemu\_login** schema and open an sql tab if one isn’t already open using this icon.

    ![image](https://github.com/AAEmu/AAEmu/assets/19890735/67c15d05-bc7f-4363-80b9-c43401bf4a6c)

9.  This first command will add a game server into the database, named AAEmu.Game, running on your local IP on port 1239

    Enter the following command into the tab and execute it.

    ```sql
    INSERT INTO `game_servers` (`id`, `name`, `host`, `port`, `hidden`) VALUES ('1', 'AAEmu.Game', '127.0.0.1', '1239', '0');
    ```

10. The second will create a login for you to use with the username and password as `test`.

    Enter the following command into the tab and execute it.

    ```sql
    INSERT INTO `users` (`id`, `username`, `password`, `email`, `last_login`, `last_ip`, `created_at`, `updated_at`) VALUES (NULL, 'test', 'n4bQgYhMfWWaL+qgxVrQFaO/TxsrC4Is0V1sFbDwCgg=', '', '0', '', '0', '0');
    ```

11. Well done, you have now setup your MySQL database for AAEmu.

#### Game Server Configuration

1.  **Build the project**. Open the command prompt in the location where you downloaded from the repository and run the following command:

    ```powershell
    dotnet build
    ```

    Result should be like below:

    ![image](https://github.com/AAEmu/AAEmu/assets/19890735/3cae3135-b365-48ac-89d3-4d6ee6efd0b0)

2.  Go to the location where you downloaded from the repository and enter into the `AAEmu.Game\bin\Debug\net10.0` folder

3.  Find the `exampleconfig.json` file, rename it to `Config.json` and update its contents

    Change the values to fit your system.

    The file contents show look similar to the below. **Change the user and password info to what you setup in your MySQL installation.**

    ```json
    {
        "Id": 1,
        "AdditionalesId": [],
        "SecretKey": "test",
        "Network": {
            "Host": "127.0.0.1",
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
        "Connections": {
            "MySQLProvider": {
                "Host": "localhost",
                "Port": "3306",
                "User": "change to your user name",
                "Password": "change to your user password",
                "Database": "aaemu_game_3030"
            }
        },
        "CharacterNameRegex": "^[a-zA-Z0-9а-яА-Я]{1,18}$",
        "MaxConcurencyThreadPool": 8,
        "HeightMapsEnable": true,
        "DefaultLanguage": "en_us"
    }
    ```

4.  Find the file `AAEmu.Game\bin\Debug\net10.0\Configurations\ClientData.json` and open it

5.  Locate the **root folder path** where you extracted the Archeage Client, copy the full path to the `game_pak` file (should be the biggest file in the folder)

    ![image](https://github.com/AAEmu/AAEmu/assets/19890735/8057a89d-d773-4294-a5ed-1c22dfaf69ef)

6.  Add the path as one of the `Source` options in the `ClientData.json` file like the following:
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
    Note the path here uses forward slashes, and the last entry does not require a comma at the end.

#### Login Server Configuration

1.  Go to the location where you downloaded from the repository and enter into the `AAEmu.Login\bin\Debug\net10.0` folder

2.  Find the `exampleconfig.json` file, rename it to `config.json` and update its contents

    Change the values to fit your system.

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
            "Host": "127.0.0.1",
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

1.  Go the folder where you extracted the Launcher and open it.

    ![image](https://github.com/AAEmu/AAEmu/assets/19890735/4b428cf6-d342-4e06-bb09-47f62d547117)

2.  Click in the `Path to Game` input, locate your Archeage Client folder within the `bin32` folder and select the `archeage.exe` file like the following:

    ![image](https://github.com/AAEmu/AAEmu/assets/19890735/b04ec3c9-9d30-44cc-ad0c-69c7837c5a63)

#### Running the servers

Start the servers in the following order:

1.  Go to the location where you downloaded from the repository and enter into the `Scripts` folder

2.  Run the `StartLoginServer.bat`

3.  Run the `StartGameServer.bat`

4.  After a few moments you should see similar outputs in the command prompt windows:

    ![image](https://github.com/AAEmu/AAEmu/assets/19890735/bc6752b9-df83-45c4-8e71-4a696b1b88c3)

#### Playing the game

1.  Open the Launcher and configure your username and password

    By default you should use `test` on both.

    **You can change this if needed in the MySQL aaemu\_login.users table**

    ![image](https://github.com/AAEmu/AAEmu/assets/19890735/d3ab9caf-6452-4e2d-8b7b-297519485788)

2.  Click in the `Play` button and you should see the Archeage Client starting.

**Happy playing! 🥳🥳🥳**
