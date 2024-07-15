-- Author: NLObP - 2024/07/15
-- Исправляем загрузку сервера для базы клиента 3.0.4.2
-- Ошибка: System.Collections.Generic.KeyNotFoundException: "The given key '26' was not present in the dictionary."
-- Изначально не хватает этой строки в таблице. Добавляем её.
-- Fixing server loading for client database 3.0.4.2
-- Error: System.Collections.Generic.KeyNotFoundException: "The given key '26' was not present in the dictionary."
-- Initially, this line is missing in the table. Let's add it.
INSERT INTO item_look_convert_required_items (id, item_count, item_look_convert_id, item_id)
VALUES (31, 9, 26, 29880);