# Introduction
We made this little docker installation script in powershell or bash - depending on your operating system to in fact simplify two things:
- Development Testing.
- New people to this project who wants a server to run quicker and with less headache even if it's easy if you know your basics.

## Prerequisites
Get the **compact.sqlite3** server database ([Google Drive](https://drive.google.com/file/d/18Nm_Q7OgWOfdw_8Xl4TBXa1Z51uGHEIh/view) or [MEGA](https://mega.nz/file/ujhFAaIS#disveSrjdUVjY9mZ3Q2xJ2b7I4te2gwbKnzMYD8HLZ4)) and the **game_pak** of the ArcheAge client version you are using. And put them where the script tell you too.

**Windows**:
- Git : https://git-scm.com/downloads
- Docker Desktop : https://www.docker.com/products/docker-desktop/

**Linux**:
- Git
- Docker
- Docker Compose **and not docker-compose for the script!**

## How to use it? First Installation
- Step 1 : Clone the project.
- Step 2 : Go to the project and the Scripts folder.
- Step 3 : Run, depending on your platform : 
  - **Windows**: `docker-install-local.ps1`
  - **Linux**: `docker-install-local.sh`
- Step 4 : Follow the step and enjoy!

## How to use it? Update
- Step 1 : Go to the project and the Scripts folder.
- Step 2 : Run, depending on your platform: 
  - Windows `docker-update-local.ps1`
  - Linux `docker-update-local.sh`

## How to use it? Launch
If you are primary a new person or not developing the project, use the Alpha method because otherwise you'll have to keep your CLI open.

- On **Windows**: Go to the project, _Shift + Right Click around > Open Powershell Window here_ (or something similar) and type, depending of what you want:
  - **Alpha**: `docker compose up -d` and check the status in your Docker Desktop, GUI or CLI of choice.
  - **Dev**: `docker compose watch` or `docker compose up --watch` for more information without GUI, it will allow hot-reloading the container if any changes are made to AAEmu.Login or AAEmu.Game.
- On Linux: Go to the project using cd or the file explorer and open a shell here.
  - **Alpha**: `docker compose up -d` and check the status in your GUI or CLI of choice.
  - **Dev**: `docker compose watch` or `docker compose up --watch` for more information without GUI, it will allow hot-reloading the container if any changes are made to AAEmu.Login or AAEmu.Game.

Now just login using AAEmu.Launcher on the 127.0.0.1 with the account of your choice. It will be created automatically.

## I got problems wtf!
- If you can't even start the installation script because of permission settings, then first use the following Powershell command `Set-ExecutionPolicy -ExecutionPolicy Unrestricted -Scope CurrentUser` to allow execution for you.
- Did you put the compact.sqlite3 database and the game_pak at the right location as requested by the script?
- Is docker really running?
- Did you launch the command `docker compose up -d` - or the other one - in the good shell? For **Windows use Powershell** and for **Linux use Bash**.
- Check the issues, check around you or even ask questions.

### Bonus
If you want to improve the scripts, feel free!
- CLI : Command Line Interface.
- GUI : Graphical User Interface.
- Docker : A beautiful tool that allow you to "containerize" almost everything.
- Docker Compose : A second beautiful tool that allow you to script your containers and such.
- Git : A versioning tool we work with.
