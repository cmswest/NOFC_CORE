<?php
include('../../../config.php');
if (!class_exists("SQLManager")) require_once($FANNIE_ROOT."src/SQLManager.php");
include($FANNIE_ROOT.'src/Credentials/projects.wfc.php');

require($FANNIE_ROOT.'auth/login.php');
if (!validateUser('admin')){
  return;
}

$projID = $_GET['projID'];

$date = date("Y-m-d");

$q = "update projects set status=2,completeDate='$date' where projID=$projID";
$r = $sql->query($q);

// build email 'to' all interested parties
$q = "select email from project_parties where projID = $projID";
$r = $sql->query($q);
$to_string = 'it@wholefoods.coop';
if ($sql->num_rows($r) > 0){
  while($row = $sql->fetch_array($r)){
    $to_string .= ", ".$row[0]."@wholefoods.coop";
  }
}


$descQ = "select projDesc from projects where projID='$projID'";
$descR = $sql->query($descQ);
$descW = $sql->fetch_array($descR);
$projDesc = $descW[0];

// mail notification
$subject = "Completed project: $projDesc";
$message = wordwrap("The project $projDesc has been marked completed.  http://key/it/projects/project.php?projID=$projID", 70);
$headers = "From: automail@wholefoods.coop";
mail($to_string,$subject,$message,$headers);

header("Location: index.php");

?>
