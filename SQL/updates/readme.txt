When your code update needs any changes to the MySQL tables, you will need to put a update script here, and also apply it in the initial sql files one folder up from here.
Files need to be named in the following format;

YYYY-MM-DD_aaemu_XXXX*.sql

Where YYY-MM-DD is the date you introduced this change, and "XXXX" is either "login" or "game".
These scripts will get executed only one time at the start of running the Login or Game server. Installed scripts get logged into a updates table to the relevant to the server it's running for.
A server will only check and run scripts that are relevant to themselves, therefor the naming of the files is important.

When running a server that supports this new update system for the first time, it will assume that all related scripts in this folder were already installed, and will be marked as such.

The date part is technically not required, but it is used to handle update order, so please keep it consistent.
