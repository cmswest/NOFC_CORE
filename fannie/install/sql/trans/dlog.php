<?php
/*
View: dlog

Columns:
	tdate datetime
	register_no int
	emp_no int
	trans_no int
	upc varchar
	trans_type varchar
	trans_subtype varchar
	trans_status varchar
	department int
	quantity double
	unitPrice dbms currency
	total dbms currency
	tax int
	foodstamp int
	ItemQtty double
	memType int
	staff int
	numflag int
	charflag int
	card_no int
	trans_id int

Depends on:
	dtransactions (table)

Use:
This view presents simplified access to
dtransactions. It omits rows from canceled
transactions and testing lane(s)/cashier(s)
*/
$CREATE['trans.dlog'] = "
	CREATE VIEW dlog AS
		SELECT
		datetime AS tdate,
		register_no,
		emp_no,
		trans_no,
		upc,
		CASE WHEN (trans_subtype IN ('CP','IC') OR upc like('%000000052')) then 'T' WHEN upc = 'DISCOUNT' then 'S' else trans_type end as trans_type,
		CASE WHEN upc = 'MAD Coupon' THEN 'MA' 
		   WHEN upc like('%00000000052') THEN 'RR' ELSE trans_subtype END as trans_subtype,
		trans_status,
		department,
		quantity,
		unitPrice,
		total,
		tax,
		foodstamp,
		ItemQtty,
		memType,
		staff,
		numflag,
		charflag,
		card_no,
		trans_id,
		".$con->concat(
			$con->convert('emp_no','char'),"'-'",
			$con->convert('register_no','char'),"'-'",
			$con->convert('trans_no','char'),'')
		." as trans_num
		FROM dtransactions
		WHERE trans_status NOT IN ('D','X','Z')
		AND emp_no <> 9999 and register_no <> 99
";
?>
