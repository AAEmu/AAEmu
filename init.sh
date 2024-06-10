# OS version: ubuntu2204LTS with git and docker 
PASSWORD=your_password  # replace 'your_password' with your actual password 

git clone https://github.com/AAEmu/AAEmu.git --depth=1 
cd AAEmu
docker run -d --name=mysql_aa -p 3306:3306  -e MYSQL_ROOT_PASSWORD=$PASSWORD mysql:8.0.12

sudo apt install dotnet-sdk-8.0

mysql -u root -p$PASSWORD -h 127.0.0.1 --port=3306 -e "CREATE DATABASE aaemu_game; CREATE DATABASE aaemu_login;show DATABASES;"

mysql -u root -p$PASSWORD -h 127.0.0.1 --port=3306 aaemu_login < ./AAEmu/SQL/aaemu_login.sql

mysql -u root -p$PASSWORD -h 127.0.0.1 --port=3306 aaemu_game < ./AAEmu/SQL/aaemu_game.sql

mysql -u root -p$PASSWORD -h 127.0.0.1 --port=3306 -e "INSERT INTO `game_servers` (`id`, `name`, `host`, `port`, `hidden`) VALUES ('1', 'AAEmu.Game', '127.0.0.1', '1239', '0');"
# open firewall  port 1239 to public if need to .
