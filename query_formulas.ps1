[System.Reflection.Assembly]::LoadWithPartialName('System.Data.SQLite') | Out-Null
$dbPath = "c:/Users/gutor/Documents/AAEmu/AAEmu.Game/bin/Debug/net10.0/Data/compact.sqlite3"
$conn = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$dbPath;Read Only=True")
$conn.Open()

Write-Output "=== NPC FORMULAS (owner_type=2) ==="
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT id, kind_id, formula FROM unit_formulas WHERE owner_type_id = 2 ORDER BY kind_id"
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    $id = $reader.GetValue(0)
    $kind = $reader.GetValue(1)
    $formula = $reader.GetValue(2)
    Write-Output "id=$id kind=$kind formula=$formula"
}
$reader.Close()

Write-Output ""
Write-Output "=== MaxHealth formula variables (kind_id for MaxHealth) ==="
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT uf.id, uf.kind_id, ufv.variable_kind_id, ufv.key, ufv.value FROM unit_formulas uf JOIN unit_formula_variables ufv ON uf.id = ufv.unit_formula_id WHERE uf.owner_type_id = 2 AND uf.kind_id = 6 ORDER BY ufv.variable_kind_id, ufv.key"
$reader2 = $cmd2.ExecuteReader()
while ($reader2.Read()) {
    $fid = $reader2.GetValue(0)
    $vkind = $reader2.GetValue(2)
    $key = $reader2.GetValue(3)
    $val = $reader2.GetValue(4)
    Write-Output "formula_id=$fid var_kind=$vkind key=$key value=$val"
}
$reader2.Close()

Write-Output ""
Write-Output "=== All formula variable kinds for NPC formulas ==="
$cmd3 = $conn.CreateCommand()
$cmd3.CommandText = "SELECT uf.id, uf.kind_id, ufv.variable_kind_id, COUNT(*) as cnt FROM unit_formulas uf JOIN unit_formula_variables ufv ON uf.id = ufv.unit_formula_id WHERE uf.owner_type_id = 2 GROUP BY uf.id, uf.kind_id, ufv.variable_kind_id ORDER BY uf.kind_id, ufv.variable_kind_id"
$reader3 = $cmd3.ExecuteReader()
while ($reader3.Read()) {
    $fid = $reader3.GetValue(0)
    $kind = $reader3.GetValue(1)
    $vkind = $reader3.GetValue(2)
    $cnt = $reader3.GetValue(3)
    Write-Output "formula_id=$fid formula_kind=$kind var_kind=$vkind count=$cnt"
}
$reader3.Close()

$conn.Close()
