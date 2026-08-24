@echo off
REM Load full AAEmu premium ICS catalog + Bill wallet/catalog sync (local Docker MySQL).
setlocal
set ROOT=%~dp0..
set MYSQL=docker exec -i aaemu-mysql mysql -uroot -ppassword

echo [1/3] aaemu_bill schema...
%MYSQL% < "%ROOT%\SQL\aaemu_bill.sql"

echo [2/3] ICS catalog (example-ics-default-en.sql)...
%MYSQL% aaemu_game < "%ROOT%\SQL\examples\example-ics-default-en.sql"

echo [3/3] Bill sync + 50000 credits per account...
%MYSQL% < "%ROOT%\SQL\examples\bill_sync_from_ics.sql"

echo Done. Restart BillServer (UseMysql=true) and in-game: /ics off  then  /ics reload  then  /ics on
endlocal
