<?php
/*******************************************************************************

    Copyright 2013 Whole Foods Co-op

    This file is part of IT CORE.

    IT CORE is free software; you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation; either version 2 of the License, or
    (at your option) any later version.

    IT CORE is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    in the file license.txt along with IT CORE; if not, write to the Free Software
    Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA

*********************************************************************************/

class CashDropPreParser extends PreParser {

	function check($str){
		global $CORE_LOCAL;
		// only check & warn once per transaction
		if ($CORE_LOCAL->get('cashDropWarned') == True) return False;

		// checking one time
		$CORE_LOCAL->set('cashDropWarned',True);

		// cannot check in standalone
		if ($CORE_LOCAL->get('standalone') == 1) return False;

		// lookup cashier total
		$db = Database::mDataConnect();
		$q = sprintf("SELECT sum(-total) FROM dtransactions WHERE
			trans_subtype='CA' AND trans_status <> 'X' AND emp_no=%d",
			$CORE_LOCAL->get('CashierNo'));
		$r = $db->query($q);
		$ttl = 0;
		if ($db->num_rows($r) > 0)
			$ttl = array_pop($db->fetch_row($r));

		if ($ttl > $CORE_LOCAL->get('cashDropThreshold'))
			return True;
	}

	function parse($str){
		// modify input to trigger CashDropParser
		return 'DROPDROP'.$str;
	}
}
