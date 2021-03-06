/*******************************************************************************

    Copyright 2009 Whole Foods Co-op

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

/*************************************************************
 * SerialPortHandler
 * 	Abstract class to manage a serial port in a separate
 * thread. Allows top-level app to interact with multiple, 
 * different serial devices through one class interface.
 * 
 * Provides Stop() and SetParent(DelegateBrowserForm) functions.
 *
 * Subclasses must implement Read() and PageLoaded(Uri).
 * Read() is the main polling loop, if reading serial data is
 * required. PageLoaded(Uri) is called on every WebBrowser
 * load event and provides the Url that was just loaded.
 *
*************************************************************/
using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using CustomForms;

using USBLayer;

namespace SPH {

public class SPH_SignAndPay_USB : SerialPortHandler {

	private static String MAGELLAN_OUTPUT_DIR = "ss-output/";

	private USBWrapper usb_port;
	private bool read_continues;
	private byte[] long_buffer;
	private int long_length;
	private int long_pos;
	private FileStream usb_fs;
	private int usb_report_size;
	private bool waiting_for_data;

	private const int LCD_X_RES = 320;
	private const int LCD_Y_RES = 240;

	private const int STATE_START_TRANSACTION = 1;
	private const int STATE_SELECT_CARD_TYPE = 2;
	private const int STATE_ENTER_PIN = 3;
	private const int STATE_WAIT_FOR_CASHIER = 4;
	private const int STATE_SELECT_EBT_TYPE = 5;
	private const int STATE_SELECT_CASHBACK = 6;
	private const int STATE_MANUAL_PAN = 11;
	private const int STATE_MANUAL_EXP = 12;
	private const int STATE_MANUAL_CVV = 13;

	private const int BUTTON_CREDIT = 5;
	private const int BUTTON_DEBIT = 6;
	private const int BUTTON_EBT = 7;
	private const int BUTTON_GIFT = 8;
	private const int BUTTON_EBT_FOOD = 9;
	private const int BUTTON_EBT_CASH = 10;
	private const int BUTTON_000  = 0;
	private const int BUTTON_500  = 5;
	private const int BUTTON_1000 = 10;
	private const int BUTTON_2000 = 20;
	private const int BUTTON_3000 = 30;
	private const int BUTTON_4000 = 40;
	private const int BUTTON_HARDWARE_BUTTON = 0xff;

	private int current_state;
	private int ack_counter;

	private string usb_devicefile;

	public SPH_SignAndPay_USB(string p) : base(p){ 
		read_continues = false;
		long_length = 0;
		long_pos = 0;
		ack_counter = 0;
		usb_fs = null;
		
		#if MONO
		usb_devicefile = p;
		#else
		int vid = 0xacd;
		int pid = 0x2310;
		usb_devicefile = string.Format("{0}&{1}",vid,pid);
		#endif
	}

	private void OpenDevice(){
		#if MONO
		usb_port = new USBWrapper_Posix();
		usb_report_size = 64;
		#else
		usb_port = new USBWrapper_Win32();
		usb_report_size = 65;
		#endif
		while(usb_fs == null){
			usb_fs = usb_port.GetUSBHandle(usb_devicefile,usb_report_size);
			if (usb_fs == null){
				if (this.verbose_mode > 0)
					System.Console.WriteLine("No device");
				System.Threading.Thread.Sleep(5000);
			}
			else { 
				if (this.verbose_mode > 0)
					System.Console.WriteLine("USB device found");
			}
			
		}

	}

	public override void Read(){ 
		OpenDevice();
		waiting_for_data = true;
		SendReport(BuildCommand(LcdSetBacklightTimeout(0)));
		SetStateStart();
		#if MONO
		MonoRead();
		#else
		ReRead();
		#endif
	}

	private void SetStateStart(){
		PushOutput("TERMCLEARALL");
		SendReport(BuildCommand(LcdStopCapture()));
		SendReport(BuildCommand(PinpadCancelGetPIN()));
		SendReport(BuildCommand(LcdFillColor(0xff,0xff,0xff)));
		SendReport(BuildCommand(LcdFillRectangle(0,0,LCD_X_RES-1,LCD_Y_RES-1)));

		SendReport(BuildCommand(LcdTextFont(3,12,14)));
		SendReport(BuildCommand(LcdTextColor(0,0,0)));
		SendReport(BuildCommand(LcdTextBackgroundColor(0xff,0xff,0xff)));
		SendReport(BuildCommand(LcdTextBackgroundMode(false)));
		SendReport(BuildCommand(LcdDrawText("Swipe Card",75,100)));

		current_state = STATE_START_TRANSACTION;
	}

	private void SetStateReStart(){
		PushOutput("TERMCLEARALL");
		SendReport(BuildCommand(LcdStopCapture()));
		SendReport(BuildCommand(PinpadCancelGetPIN()));
		SendReport(BuildCommand(LcdFillColor(0xff,0xff,0xff)));
		SendReport(BuildCommand(LcdFillRectangle(0,0,LCD_X_RES-1,LCD_Y_RES-1)));

		SendReport(BuildCommand(LcdTextFont(3,12,14)));
		SendReport(BuildCommand(LcdTextColor(0,0,0)));
		SendReport(BuildCommand(LcdTextBackgroundColor(0xff,0xff,0xff)));
		SendReport(BuildCommand(LcdTextBackgroundMode(false)));
		SendReport(BuildCommand(LcdDrawText("Error",115,70)));
		SendReport(BuildCommand(LcdDrawText("Swipe Card Again",55,100)));

		current_state = STATE_START_TRANSACTION;
	}

	private void SetStateCardType(){
		SendReport(BuildCommand(LcdFillColor(0xff,0xff,0xff)));
		SendReport(BuildCommand(LcdFillRectangle(0,0,LCD_X_RES-1,LCD_Y_RES-1)));

		SendReport(BuildCommand(LcdStopCapture()));
		SendReport(BuildCommand(LcdClearSignature()));
		SendReport(BuildCommand(LcdSetClipArea(0,0,1,1)));
		SendReport(BuildCommand(LcdCreateButton(BUTTON_CREDIT,"Credit",5,5,95,95)));
		SendReport(BuildCommand(LcdCreateButton(BUTTON_DEBIT,"Debit",224,5,314,95)));
		SendReport(BuildCommand(LcdCreateButton(BUTTON_EBT,"EBT",5,144,95,234)));
		//SendReport(BuildCommand(LcdCreateButton(BUTTON_GIFT,"Gift",224,144,314,234)));
		SendReport(BuildCommand(LcdStartCapture(4)));

		current_state = STATE_SELECT_CARD_TYPE;
	}

	private void SetStateEbtType(){
		SendReport(BuildCommand(LcdFillColor(0xff,0xff,0xff)));
		SendReport(BuildCommand(LcdFillRectangle(0,0,LCD_X_RES-1,LCD_Y_RES-1)));

		SendReport(BuildCommand(LcdStopCapture()));
		SendReport(BuildCommand(LcdClearSignature()));
		SendReport(BuildCommand(LcdSetClipArea(0,0,1,1)));
		SendReport(BuildCommand(LcdCreateButton(BUTTON_EBT_FOOD,"Food Side",5,5,115,95)));
		SendReport(BuildCommand(LcdCreateButton(BUTTON_EBT_CASH,"Cash Side",204,5,314,95)));
		SendReport(BuildCommand(LcdStartCapture(4)));

		current_state = STATE_SELECT_EBT_TYPE;
	}

	private void SetStateCashBack(){
		SendReport(BuildCommand(LcdFillColor(0xff,0xff,0xff)));
		SendReport(BuildCommand(LcdFillRectangle(0,0,LCD_X_RES-1,LCD_Y_RES-1)));

		SendReport(BuildCommand(LcdStopCapture()));
		SendReport(BuildCommand(LcdClearSignature()));
		SendReport(BuildCommand(LcdSetClipArea(0,0,1,1)));
		SendReport(BuildCommand(LcdTextFont(3,12,14)));
		SendReport(BuildCommand(LcdDrawText("Select Cash Back",60,5)));
		SendReport(BuildCommand(LcdCreateButton(BUTTON_000,"None",5,40,95,130)));
		SendReport(BuildCommand(LcdCreateButton(BUTTON_500,"5.00",113,40,208,130)));
		SendReport(BuildCommand(LcdCreateButton(BUTTON_1000,"10.00",224,40,314,130)));
		SendReport(BuildCommand(LcdCreateButton(BUTTON_2000,"20.00",5,144,95,234)));
		SendReport(BuildCommand(LcdCreateButton(BUTTON_3000,"30.00",113,144,208,234)));
		SendReport(BuildCommand(LcdCreateButton(BUTTON_4000,"40.00",224,144,314,234)));
		SendReport(BuildCommand(LcdStartCapture(4)));

		current_state = STATE_SELECT_CASHBACK;
	}

	private void SetStateGetPin(){
		SendReport(BuildCommand(LcdStopCapture()));
		ack_counter = 0;

		SendReport(BuildCommand(PinpadGetPIN()));
		current_state = STATE_ENTER_PIN;
	}

	private void SetStateWaitForCashier(){
		SendReport(BuildCommand(LcdStopCapture()));
		SendReport(BuildCommand(LcdFillColor(0xff,0xff,0xff)));
		SendReport(BuildCommand(LcdFillRectangle(0,0,LCD_X_RES-1,LCD_Y_RES-1)));

		SendReport(BuildCommand(LcdTextFont(3,12,14)));
		SendReport(BuildCommand(LcdTextColor(0,0,0)));
		SendReport(BuildCommand(LcdTextBackgroundColor(0xff,0xff,0xff)));
		SendReport(BuildCommand(LcdTextBackgroundMode(false)));
		SendReport(BuildCommand(LcdDrawText("wait for cashier",75,100)));

		current_state = STATE_WAIT_FOR_CASHIER;
	}

	private void SetStateApproved(){
		SendReport(BuildCommand(LcdStopCapture()));
		SendReport(BuildCommand(LcdFillColor(0xff,0xff,0xff)));
		SendReport(BuildCommand(LcdFillRectangle(0,0,LCD_X_RES-1,LCD_Y_RES-1)));

		SendReport(BuildCommand(LcdTextFont(3,12,14)));
		SendReport(BuildCommand(LcdTextColor(0,0,0)));
		SendReport(BuildCommand(LcdTextBackgroundColor(0xff,0xff,0xff)));
		SendReport(BuildCommand(LcdTextBackgroundMode(false)));
		SendReport(BuildCommand(LcdDrawText("approved",75,100)));
	}

	private void SetStateGetManualPan(){
		SendReport(BuildCommand(LcdStopCapture()));
		ack_counter = 0;
		SendReport(BuildCommand(ManualEntryPAN()));
		current_state = STATE_MANUAL_PAN;
	}

	private void SetStateGetManualExp(){
		ack_counter = 0;
		SendReport(BuildCommand(ManualEntryExp()));
		current_state = STATE_MANUAL_EXP;
	}

	private void SetStateGetManualCVV(){
		ack_counter = 0;
		SendReport(BuildCommand(ManualEntryCVV()));
		current_state = STATE_MANUAL_CVV;
	}

	private void HandleReadData(byte[] input){
		int msg_sum = 0;
		if (usb_report_size == 64){
			byte[] temp_in = new byte[65];
			temp_in[0] = 0;
			for (int i=0; i < input.Length; i++){
				temp_in[i+1] = input[i];
				msg_sum += input[i];
			}
			input = temp_in;
		}

		/* Data received, as bytes
		*/
		if (this.verbose_mode > 1){
			System.Console.WriteLine("");
			System.Console.WriteLine("IN BYTES:");
			for(int i=0;i<input.Length;i++){
				if (i>0 && i %16==0) System.Console.WriteLine("");
				System.Console.Write("{0:x} ",input[i]);
			}
			System.Console.WriteLine("");
			System.Console.WriteLine("");
		}

		int report_length = input[1] & (0x80-1);
		/*
		 * Bit 7 turned on means a multi-report message
		 */
		if ( (input[1] & 0x80) != 0){
			read_continues = true;
		}

		byte[] data = null;
		if (report_length > 3 && (long_pos > 0 || input[2] == 0x02)){ // protcol messages
			int data_length = input[3] + (input[4] << 8);

			int d_start = 5;
			if (input[d_start] == 0x6 && data_length > 1){ 
				d_start++; // ACK byte
				data_length--;
			}

			/*
			 * New multi-report message; init class members
			 */
			if (read_continues && long_length == 0){
				long_length = data_length;
				long_pos = 0;
				long_buffer = new byte[data_length];
				if (data_length > report_length)
					data_length = report_length-3;
			}
			else if (read_continues){
				// subsequent messages start immediately after
				// report ID & length fields
				d_start = 2;
			}

			if (data_length > report_length) data_length = report_length;

			data = new byte[data_length];
			for (int i=0; i<data_length && i+d_start<report_length+2;i++)
				data[i] = input[i+d_start];

			/**
			 * Append data from multi-report messages to the
			 * class member byte buffer
			 */
			if (read_continues){
				// last message will contain checksum bytes and
				// End Tx byte, so don't copy entire data array
				int d_len = ((input[1]&0x80)!=0) ? data.Length : data.Length-3;
				for(int i=0;i<d_len;i++){
					long_buffer[long_pos++] = data[i];
				}
			}

			if (this.verbose_mode > 1){
				System.Console.Write("Received: ");
				foreach(byte b in data)
					System.Console.Write((char)b);
				System.Console.WriteLine("");
			}
		}
		else if (report_length > 3){ // non-protcol messages
			data = new byte[report_length];
			for(int i=0; i<report_length && i+2<input.Length; i++){
				data[i] = input[i+2];
			}
		}

		if ( (input[1] & 0x80) == 0){
			if (long_buffer != null){
				if (this.verbose_mode > 0){
					System.Console.Write("Big Msg: ");
					foreach(byte b in long_buffer)
						System.Console.Write((char)b);
					System.Console.WriteLine("");
				}

				HandleDeviceMessage(long_buffer);
			}
			else {
				HandleDeviceMessage(data);
			}
			read_continues = false;
			long_length = 0;
			long_pos = 0;
			long_buffer = null;
		}
	}

	/**
	  Proper async version. Call ReRead at the end to
	  start another async read.
	*/
	private void ReadCallback(IAsyncResult iar){
		byte[] input = (byte[])iar.AsyncState;
		try {
			usb_fs.EndRead(iar);
			HandleReadData(input);		
		}
		catch (Exception ex){
			if (this.verbose_mode > 0)
				System.Console.WriteLine(ex);
		}

		ReRead();
	}

	/**
	  Synchronous version. Do not automatically start
	  another read. The calling method will handle that.
	
	  The wait on excpetion is important. Exceptions
	  are generally the result of a write that occurs
	  during a blocking read. Waiting a second lets any
	  subsequent writes complete without more blocking.
	*/
	private void MonoReadCallback(IAsyncResult iar){
		byte[] input = (byte[])iar.AsyncState;
		try {
			usb_fs.EndRead(iar);
			HandleReadData(input);		
		}
		catch (Exception ex){
			if (this.verbose_mode > 0)
				System.Console.WriteLine(ex);
			System.Threading.Thread.Sleep(1000);
		}
	}

	private void HandleDeviceMessage(byte[] msg){
		if (this.verbose_mode > 0)
			System.Console.Write("DMSG: {0}: ",current_state);

		//if (msg == null) return;

		if (this.verbose_mode > 0){
			foreach(byte b in msg)
				System.Console.Write("{0:x} ",b);
			System.Console.WriteLine();
		}
		switch(current_state){
		case STATE_SELECT_CARD_TYPE:
			if (msg.Length == 4 && msg[0] == 0x7a){
				SendReport(BuildCommand(DoBeep()));
				if (msg[1] == BUTTON_CREDIT){
					PushOutput("TERM:Credit");
					SetStateWaitForCashier();
				}
				else if (msg[1] == BUTTON_DEBIT){
					PushOutput("TERM:Debit");
					SetStateCashBack();
				}
				else if (msg[1] == BUTTON_EBT){
					SetStateEbtType();
				}
				else if (msg[1] == BUTTON_GIFT){
					PushOutput("TERM:Gift");
					SetStateWaitForCashier();	
				}
				else if (msg[1] == BUTTON_HARDWARE_BUTTON && msg[3] == 0x43){
					SetStateStart();
				}
			}
			break;
		case STATE_SELECT_EBT_TYPE:
			if (msg.Length == 4 && msg[0] == 0x7a){
				SendReport(BuildCommand(DoBeep()));
				if (msg[1] == BUTTON_EBT_FOOD){
					PushOutput("TERM:EbtFood");
					SetStateGetPin();
				}
				else if (msg[1] == BUTTON_EBT_CASH){
					PushOutput("TERM:EbtCash");
					SetStateCashBack();
				}
				else if (msg[1] == BUTTON_HARDWARE_BUTTON && msg[3] == 0x43){
					SetStateStart();
				}
			}
			break;
		case STATE_SELECT_CASHBACK:
			if (msg.Length == 4 && msg[0] == 0x7a){
				SendReport(BuildCommand(DoBeep()));
				if (msg[1] == BUTTON_HARDWARE_BUTTON && msg[3] == 0x43){
					SetStateStart();
				}
				else {
					PushOutput("TERMCB:"+msg[1]);
					SetStateGetPin();
				}
			}
			break;
		case STATE_ENTER_PIN:
			if (msg.Length == 3 && msg[0] == 0x15){
				SetStateStart();
			}
			else if (msg.Length == 36){
				string pinhex = "";
				foreach(byte b in msg)
					pinhex += ((char)b);
				PushOutput("PINCACHE:"+pinhex);
				SetStateWaitForCashier();
			}
			break;
		case STATE_MANUAL_PAN:
			if (msg.Length == 1 && msg[0] == 0x6){
				ack_counter++;
				if (this.verbose_mode > 0)
					System.Console.WriteLine(ack_counter);
				if (ack_counter == 1)
					SetStateGetManualExp();
			}
			else if (msg.Length == 3 && msg[0] == 0x15){
				SetStateStart();
			}
			break;
		case STATE_MANUAL_EXP:
			if (msg.Length == 1 && msg[0] == 0x6){
				ack_counter++;
				if (this.verbose_mode > 0)
					System.Console.WriteLine(ack_counter);
				if (ack_counter == 2)
					SetStateGetManualCVV();
			}
			else if (msg.Length == 3 && msg[0] == 0x15){
				SetStateStart();
			}
			break;
		case STATE_MANUAL_CVV:
			if (msg.Length > 63 && msg[0] == 0x80){
				string block = FixupCardBlock(msg);
				PushOutput("PANCACHE:"+block);
				SetStateCardType();
			}

			else if (msg.Length == 3 && msg[0] == 0x15){
				SetStateStart();
			}
			break;
		case STATE_START_TRANSACTION:
			if (msg.Length > 63 && msg[0] == 0x80 ){
				SendReport(BuildCommand(DoBeep()));
				string block = FixupCardBlock(msg);
				if (block.Length == 0){
					SetStateReStart();
				}
				else {
					PushOutput("PANCACHE:"+block);
					SetStateCardType();
				}
			}
			else if (msg.Length > 1){
				if (this.verbose_mode > 0)
					System.Console.WriteLine(msg.Length+" "+msg[0]+" "+msg[1]);
			}
			break;
		default:
			break;
		}
	}

	/**
	 * Convert encrypted card block to a hex string
	 * and add the serial protcol controcl characters back
	 * to the beginning and end. PHP-side software expects
	 * this format
	 */
	private string FixupCardBlock(byte[] data){
		string hex = BitConverter.ToString(data).Replace("-","");
		hex = "02E600"+hex+"XXXX03";
		if (hex.Length < 24) return "";
		if(hex.Substring(hex.Length-16,10) == "0000000000") return "";
		if (this.verbose_mode > 0)
			System.Console.WriteLine(hex);
		return hex;
	}

	private void ReRead(){
		waiting_for_data = true;
		byte[] buf = new byte[usb_report_size];
		usb_fs.BeginRead(buf, 0, usb_report_size, new AsyncCallback(ReadCallback), buf);
	}

	/**
	  Mono doesn't support asynchronous reads correctly.
	  BeginRead will block. Using ReRead with Mono will
	  eventually make the stack blow up as ReRead and
	  ReadCallback calls build up one after the other.
	*/
	private void MonoRead(){
		waiting_for_data = true;
		while(SPH_Running){
			byte[] buf = new byte[usb_report_size];
			usb_fs.BeginRead(buf, 0, usb_report_size, new AsyncCallback(MonoReadCallback), buf);
		}
	}

	public override void HandleMsg(string msg){ 
		switch(msg){
		case "termReset":
			SetStateStart();
			break;
		case "termManual":
			SetStateGetManualPan();
			break;
		case "termApproved":
			SetStateApproved();
			break;
		}
	}

	/**
	 * Wrap command in proper leading and ending bytes
	 */
	private byte[] BuildCommand(byte[] data){
		int size = data.Length + 6;
		if (data.Length > 0x8000) size++;

		byte[] cmd = new byte[size];

		int pos = 0;
		cmd[pos] = 0x2;
		pos++;

		if (data.Length > 0x8000){
			cmd[pos] = (byte)(data.Length & 0xff);
			pos++;
			cmd[pos] = (byte)((data.Length >> 8) & 0xff);
			pos++;
			cmd[pos] = (byte)((data.Length >> 16) & 0xff);
			pos++;
		}
		else {
			cmd[pos] = (byte)(data.Length & 0xff);
			pos++;
			cmd[pos] = (byte)((data.Length >> 8) & 0xff);
			pos++;
		}

		for (int i=0; i < data.Length; i++){
			cmd[pos+i] = data[i];
		}
		pos += data.Length;

		cmd[pos] = LrcXor(data);
		pos++;

		cmd[pos] = LrcSum(data);
		pos++;

		cmd[pos] = 0x3;

		return cmd;
	}

	/**
	 * LRC type 1: sum of data bytes
	 */
	private byte LrcSum(byte[] data){
		int lrc = 0;
		foreach(byte b in data)
			lrc = (lrc + b) & 0xff;
		return (byte)lrc;
	}

	/**
	 * LRC type 2: xor of data bytes
	 */
	private byte LrcXor(byte[] data){
		byte lrc = 0;
		foreach(byte b in data)
			lrc = (byte)(lrc ^ b);
		return lrc;
	}

	/**
	 * Write to device in formatted reports
	 */
	private void SendReport(byte[] data){
		if (this.verbose_mode > 0){
			System.Console.WriteLine("Full Report "+data.Length);
			for(int j=0;j<data.Length;j++){
				if (j % 16 == 0 && j > 0)
					System.Console.WriteLine("");
				System.Console.Write("{0:x} ",data[j]);
			}
			System.Console.WriteLine("");
			System.Console.WriteLine("");
		}

		byte[] report = new byte[usb_report_size];
		int size_field = (usb_report_size == 65) ? 1 : 0;

		for(int j=0;j<usb_report_size;j++) report[j] = 0;
		int size=0;

		for (int i=0;i<data.Length;i++){
			if (i > 0 && i % 63 == 0){
				report[size_field] = 63 | 0x80;

				if (this.verbose_mode > 1){
					for(int j=0;j<usb_report_size;j++){
						if (j % 16 == 0 && j > 0)
							System.Console.WriteLine("");
						System.Console.Write("{0:x} ", report[j]);
					}
					System.Console.WriteLine("");
					System.Console.WriteLine("");
				}

				usb_fs.Write(report,0,usb_report_size);
				System.Threading.Thread.Sleep(100);

				for(int j=0;j<usb_report_size;j++) report[j] = 0;
				size=0;
			}
			report[(i%63)+size_field+1] = data[i];
			size++;
		}

		report[size_field] = (byte)size;

		if (this.verbose_mode > 1){
			for(int i=0;i<usb_report_size;i++){
				if (i % 16 == 0 && i > 0)
					System.Console.WriteLine("");
				System.Console.Write("{0:x} ", report[i]);
			}
			System.Console.WriteLine("");
			System.Console.WriteLine("");
		}

		usb_fs.Write(report,0,usb_report_size);
		System.Threading.Thread.Sleep(100);
	}

	private void PushOutput(string s){
		int ticks = Environment.TickCount;
		char sep = System.IO.Path.DirectorySeparatorChar;
		while(File.Exists(MAGELLAN_OUTPUT_DIR+sep+ticks))
			ticks++;

		TextWriter sw = new StreamWriter(MAGELLAN_OUTPUT_DIR+sep+"tmp"+sep+ticks);
		sw = TextWriter.Synchronized(sw);
		sw.WriteLine(s);
		sw.Close();
		File.Move(MAGELLAN_OUTPUT_DIR+sep+"tmp"+sep+ticks,
			  MAGELLAN_OUTPUT_DIR+sep+ticks);
	}

	/**
	 * Device Command Functions
	 */

	private byte[] LcdTextFont(int charset, int width, int height){
		byte[] ret = new byte[9];

		// command head
		ret[0] = 0x8a;
		ret[1] = 0x46;
		ret[2] = 0x40;

		ret[3] = (byte)(0xff & height);
		ret[4] = (byte)(0xff & width);
		ret[5] = 0x0; // bold
		ret[6] = 0x0; // italic
		ret[7] = 0x0; // underlined
		ret[8] = (charset > 6) ? (byte)6 : (byte)charset;

		return ret;
	}

	private byte[] LcdTextColor(int red, int green, int blue){
		byte[] ret = new byte[6];

		// Command head
		ret[0] = 0x8a;
		ret[1] = 0x46;
		ret[2] = 0x41;

		ret[3] = (byte)(red & 0xff);
		ret[4] = (byte)(green & 0xff);
		ret[5] = (byte)(blue & 0xff);

		return ret;
	}

	private byte[] LcdTextBackgroundColor(int red, int green, int blue){
		byte[] ret = new byte[6];

		// Command head
		ret[0] = 0x8a;
		ret[1] = 0x46;
		ret[2] = 0x42;

		ret[3] = (byte)(red & 0xff);
		ret[4] = (byte)(green & 0xff);
		ret[5] = (byte)(blue & 0xff);

		return ret;
	}

	private byte[] LcdTextBackgroundMode(bool is_transparent){
		byte[] ret = new byte[4];

		// Command head
		ret[0] = 0x8a;
		ret[1] = 0x46;
		ret[2] = 0x43;
		
		ret[3] = (is_transparent) ? (byte)1 : (byte)0;

		return ret;
	}

	private byte[] LcdDrawText(string text, int x, int y){
		byte[] ret = new byte[9 + text.Length];

		// Command head
		ret[0] = 0x8a;
		ret[1] = 0x46;
		ret[2] = 0x4f;

		ret[3] = (byte)(x & 0xff);
		ret[4] = (byte)( (x >> 8) & 0xff);

		ret[5] = (byte)(y & 0xff);
		ret[6] = (byte)( (y >> 8) & 0xff);

		ret[7] = (byte)(text.Length & 0xff);
		ret[8] = (byte)( (text.Length >> 8) & 0xff);

		int pos = 9;
		foreach(byte b in System.Text.Encoding.ASCII.GetBytes(text)){
			ret[pos] = b;
			pos++;
		}

		return ret;
	}

	private byte[] LcdDrawTextInRectangle(string text, int x_top_left, int y_top_left,
			int x_bottom_right, int y_bottom_right){

		byte[] ret = new byte[13 + text.Length];

		// Command head
		ret[0] = 0x8a;
		ret[1] = 0x46;
		ret[2] = 0x4e;

		ret[3] = (byte)(x_top_left & 0xff);
		ret[4] = (byte)( (x_top_left >> 8) & 0xff);

		ret[5] = (byte)(y_top_left & 0xff);
		ret[6] = (byte)( (y_top_left >> 8) & 0xff);

		ret[7] = (byte)(x_bottom_right & 0xff);
		ret[8] = (byte)( (x_bottom_right >> 8) & 0xff);

		ret[9] = (byte)(y_bottom_right & 0xff);
		ret[10] = (byte)( (y_bottom_right >> 8) & 0xff);

		ret[11] = (byte)(text.Length & 0xff);
		ret[12] = (byte)( (text.Length >> 8) & 0xff);

		int pos = 13;
		foreach(byte b in System.Text.Encoding.ASCII.GetBytes(text)){
			ret[pos] = b;
			pos++;
		}

		return ret;
	}

	private byte[] LcdFillColor(int red, int green, int blue){
		byte[] ret = new byte[6];

		// Command head
		ret[0] = 0x8a;
		ret[1] = 0x46;
		ret[2] = 0x20;

		ret[3] = (byte)(0xff & red);
		ret[4] = (byte)(0xff & green);
		ret[5] = (byte)(0xff & blue);

		return ret;
	}

	private byte[] LcdFillRectangle(int x_top_left, int y_top_left,
			int x_bottom_right, int y_bottom_right){
		byte[] ret = new byte[11];

		// Command head
		ret[0] = 0x8a;
		ret[1] = 0x46;
		ret[2] = 0x22;

		ret[3] = (byte)(x_top_left & 0xff);
		ret[4] = (byte)( (x_top_left >> 8) & 0xff);

		ret[5] = (byte)(y_top_left & 0xff);
		ret[6] = (byte)( (y_top_left >> 8) & 0xff);

		ret[7] = (byte)(x_bottom_right & 0xff);
		ret[8] = (byte)( (x_bottom_right >> 8) & 0xff);

		ret[9] = (byte)(y_bottom_right & 0xff);
		ret[10] = (byte)( (y_bottom_right >> 8) & 0xff);

		return ret;
	}

	/**
	 * @param interval is five second periods. 
	 *    1=> 5 seconds, 2=> 10 seconds, etc
	 *    0=> always on
	 */
	private byte[] LcdSetBacklightTimeout(int interval){
		byte[] ret = new byte[5];

		// Command head
		ret[0] = 0x8a;
		ret[1] = 0x53;
		ret[2] = 0x80;

		ret[3] = 0x1;
		ret[4] = (byte)(interval & 0xff);

		return ret;
	}

	private byte[] LcdSetClipArea(int x_top_left, int y_top_left, int x_bottom_right, int y_bottom_right){
		byte[] ret = new byte[15];

		// Command head
		ret[0] = 0x7a;
		ret[1] = 0x46;
		ret[2] = 0x03;

		ret[3] = (byte)(x_top_left & 0xff);
		ret[4] = (byte)( (x_top_left >> 8) & 0xff);

		ret[5] = (byte)(y_top_left & 0xff);
		ret[6] = (byte)( (y_top_left >> 8) & 0xff);

		ret[7] = (byte)(x_bottom_right & 0xff);
		ret[8] = (byte)( (x_bottom_right >> 8) & 0xff);

		ret[9] = (byte)(y_bottom_right & 0xff);
		ret[10] = (byte)( (y_bottom_right >> 8) & 0xff);

		ret[11] = 0x0; // don't show lines around area

		// rgb for border lines
		ret[12] = 0x0;
		ret[13] = 0x0;
		ret[14] = 0x0;

		return ret;
	}

	private byte[] LcdCreateButton(int id, string label, int x_top_left, int y_top_left,
			int x_bottom_right, int y_bottom_right){

		byte[] ret = new byte[16 + label.Length];

		// Command head
		ret[0] = 0x7a;
		ret[1] = 0x46;
		ret[2] = 0x04;

		ret[3] = (byte)(id & 0xff);
		ret[4] = 0x1; // button is type 1
		ret[5] = 0xf;

		ret[6] = (byte)(x_top_left & 0xff);
		ret[7] = (byte)( (x_top_left >> 8) & 0xff);

		ret[8] = (byte)(y_top_left & 0xff);
		ret[9] = (byte)( (y_top_left >> 8) & 0xff);

		ret[10] = (byte)(x_bottom_right & 0xff);
		ret[11] = (byte)( (x_bottom_right >> 8) & 0xff);

		ret[12] = (byte)(y_bottom_right & 0xff);
		ret[13] = (byte)( (y_bottom_right >> 8) & 0xff);

		ret[14] = (byte)(label.Length & 0xff);
		ret[15] = (byte)( (label.Length >> 8) & 0xff);

		int pos = 16;
		foreach(byte b in System.Text.Encoding.ASCII.GetBytes(label)){
			ret[pos] = b;
			pos++;
		}

		return ret;
	}

	private byte[] LcdCalibrateTouch(){
		return new byte[3]{ 0x7a, 0x46, 0x1 };
	}

	/**
	 * Mode 5 => buffered
	 * Mode < 5 => streaming data
	 */
	private byte[] LcdStartCapture(int mode){
		byte[] ret = new byte[11];

		ret[0] = 0x7a;
		ret[1] = 0x46;
		ret[2] = 0x10;

		ret[3] = (byte)(0xff & mode);

		ret[4] = 0x7a;

		ret[5] = 0x0;
		ret[6] = 0x0;
		ret[7] = 0x0;

		ret[8] = 0xff;
		ret[9] = 0xff;
		ret[10] = 0xff;

		return ret;
	}

	private byte[] LcdClearSignature(){
		return new byte[3]{ 0x7a, 0x46, 0x19 };
	}

	private byte[] LcdStopCapture(){
		return new byte[3]{ 0x7a, 0x46, 0x1f };
	}

	private byte[] PinpadGetPIN(){
		byte[] ret = new byte[112];
		int pos = 0;

		// Command head
		ret[pos++] = 0x75;
		ret[pos++] = 0x46;
		ret[pos++] = 0x07;

		ret[pos++] = 0x31; // key type DUKPT
		ret[pos++] = 0xc; // max pin length
		ret[pos++] = 0x4; // min pin length
		ret[pos++] = 0x0; // no account #
		ret[pos++] = 0x3; // clear display, show messages

		// background color
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;
		ret[pos++] = 0xcc;

		// font setting
		ret[pos++] = 0x16;
		ret[pos++] = 0x18;
		ret[pos++] = 0;
		ret[pos++] = 0;
		ret[pos++] = 0;
		ret[pos++] = 5;

		// color for something
		ret[pos++] = 0;
		ret[pos++] = 0;
		ret[pos++] = 0;

		ret[pos++] = 0x1; // transparent

		// text color
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;

		// xy set 1
		ret[pos++] = 0x20;
		ret[pos++] = 0;
		ret[pos++] = 0x60;
		ret[pos++] = 0;

		// xy set 2
		ret[pos++] = 0x10;
		ret[pos++] = 0x1;
		ret[pos++] = 0x90;
		ret[pos++] = 0;

		ret[pos++] = 0xf; // show 4 lines
		ret[pos++] = 2; // 2 messages follow
		ret[pos++] = 0;
		ret[pos++] = 0x1f; // message length
		ret[pos++] = 0;

		// another font
		ret[pos++] = 0x10;
		ret[pos++] = 0x10;
		ret[pos++] = 0x0;
		ret[pos++] = 0x0;
		ret[pos++] = 0x0;
		ret[pos++] = 0x3;

		// text rgb
		ret[pos++] = 0x0;
		ret[pos++] = 0x0;
		ret[pos++] = 0xff;

		ret[pos++] = 0x1; // transparent

		// background rgb
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;

		// text xy
		ret[pos++] = 0x40;
		ret[pos++] = 0x0;
		ret[pos++] = 0x20;
		ret[pos++] = 0x0;

		// text length
		ret[pos++] = 0xa;
		ret[pos++] = 0;

		string msg = "Enter PIN:";
		foreach(byte b in System.Text.Encoding.ASCII.GetBytes(msg)){
			ret[pos] = b;
			pos++;
		}

		// next message length
		ret[pos++] = 0x2e;
		ret[pos++] = 0x0;

		// another font
		ret[pos++] = 0xc;
		ret[pos++] = 0xc;
		ret[pos++] = 0x0;
		ret[pos++] = 0x0;
		ret[pos++] = 0x0;
		ret[pos++] = 0x3;

		// another rgb
		ret[pos++] = 0x0;
		ret[pos++] = 0x0;
		ret[pos++] = 0xff;

		ret[pos++] = 1; // transparent

		// background rgb
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;

		// xy
		ret[pos++] = 0x6;
		ret[pos++] = 0x0;
		ret[pos++] = 0xb4;
		ret[pos++] = 0x0;

		// text length
		ret[pos++] = 0x19;
		ret[pos++] = 0x0;
		
		msg = "Press Enter Key When Done";

		foreach(byte b in System.Text.Encoding.ASCII.GetBytes(msg)){
			ret[pos] = b;
			pos++;
		}

		return ret;
	}

	private byte[] PinpadCancelGetPIN(){
		return new byte[3]{ 0x75, 0x46, 0x9 };
	}

	private byte[] ManualEntryPAN(){
		byte[] ret = new byte[68];

		int pos = 0;
		ret[pos++] = 0x75;
		ret[pos++] = 0x46;
		ret[pos++] = 0x40;

		ret[pos++] = 0x0; // card type
		ret[pos++] = 0x1; // PAN mode
		ret[pos++] = 23; // max length
		ret[pos++] = 6; // min length
		ret[pos++] = 0x3; // redraw screen

		// rgb background
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;
		
		// font setting
		ret[pos++] = 0x16;
		ret[pos++] = 0x18;
		ret[pos++] = 0;
		ret[pos++] = 0;
		ret[pos++] = 0;
		ret[pos++] = 5;

		// color for something
		ret[pos++] = 0;
		ret[pos++] = 0;
		ret[pos++] = 0;

		ret[pos++] = 0x1; // transparent

		// text color
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;

		// xy set 1
		ret[pos++] = 0x20;
		ret[pos++] = 0;
		ret[pos++] = 0x60;
		ret[pos++] = 0;

		// xy set 2
		ret[pos++] = 0x10;
		ret[pos++] = 0x1;
		ret[pos++] = 0x90;
		ret[pos++] = 0;

		ret[pos++] = 0xf; // show 4 lines
		ret[pos++] = 1; // 1 messages follow
		ret[pos++] = 0; 
		ret[pos++] = 33;
		ret[pos++] = 0; 

		// another font
		ret[pos++] = 0x10;
		ret[pos++] = 0x10;
		ret[pos++] = 0x0;
		ret[pos++] = 0x0;
		ret[pos++] = 0x0;
		ret[pos++] = 0x3;

		// text rgb
		ret[pos++] = 0x0;
		ret[pos++] = 0x0;
		ret[pos++] = 0xff;

		ret[pos++] = 0x1; // transparent

		// background rgb
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;

		// text xy
		ret[pos++] = 0x40;
		ret[pos++] = 0x0;
		ret[pos++] = 0x20;
		ret[pos++] = 0x0;

		string msg = "Enter Card #";
		ret[pos++] = 12;
		ret[pos++] = 0;

		foreach(byte b in System.Text.Encoding.ASCII.GetBytes(msg)){
			ret[pos] = b;
			pos++;
		}

		return ret;
	}

	private byte[] ManualEntryExp(){
		byte[] ret = new byte[70];

		int pos = 0;
		ret[pos++] = 0x75;
		ret[pos++] = 0x46;
		ret[pos++] = 0x40;

		ret[pos++] = 0x0; // card type
		ret[pos++] = 0x2; // exp mode
		ret[pos++] = 4; // max length
		ret[pos++] = 4; // min length
		ret[pos++] = 0x3; // redraw screen

		// rgb background
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;
		
		// font setting
		ret[pos++] = 0x16;
		ret[pos++] = 0x18;
		ret[pos++] = 0;
		ret[pos++] = 0;
		ret[pos++] = 0;
		ret[pos++] = 5;

		// color for something
		ret[pos++] = 0;
		ret[pos++] = 0;
		ret[pos++] = 0;

		ret[pos++] = 0x1; // transparent

		// text color
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;

		// xy set 1
		ret[pos++] = 0x20;
		ret[pos++] = 0;
		ret[pos++] = 0x60;
		ret[pos++] = 0;

		// xy set 2
		ret[pos++] = 0x10;
		ret[pos++] = 0x1;
		ret[pos++] = 0x90;
		ret[pos++] = 0;

		ret[pos++] = 0xf; // show 4 lines
		ret[pos++] = 1; // 1 messages follow
		ret[pos++] = 0; 
		ret[pos++] = 35;
		ret[pos++] = 0; 

		// another font
		ret[pos++] = 0x10;
		ret[pos++] = 0x10;
		ret[pos++] = 0x0;
		ret[pos++] = 0x0;
		ret[pos++] = 0x0;
		ret[pos++] = 0x3;

		// text rgb
		ret[pos++] = 0x0;
		ret[pos++] = 0x0;
		ret[pos++] = 0xff;

		ret[pos++] = 0x1; // transparent

		// background rgb
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;

		// text xy
		ret[pos++] = 0x40;
		ret[pos++] = 0x0;
		ret[pos++] = 0x20;
		ret[pos++] = 0x0;

		string msg = "Enter Exp MMYY";
		ret[pos++] = 14;
		ret[pos++] = 0;

		foreach(byte b in System.Text.Encoding.ASCII.GetBytes(msg)){
			ret[pos] = b;
			pos++;
		}

		return ret;
	}

	private byte[] ManualEntryCVV(){
		byte[] ret = new byte[75];

		int pos = 0;
		ret[pos++] = 0x75;
		ret[pos++] = 0x46;
		ret[pos++] = 0x40;

		ret[pos++] = 0x0; // card type
		ret[pos++] = 0x3; // CVV mode
		ret[pos++] = 4; // max length
		ret[pos++] = 3; // min length
		ret[pos++] = 0x3; // redraw screen

		// rgb background
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;
		
		// font setting
		ret[pos++] = 0x16;
		ret[pos++] = 0x18;
		ret[pos++] = 0;
		ret[pos++] = 0;
		ret[pos++] = 0;
		ret[pos++] = 5;

		// color for something
		ret[pos++] = 0;
		ret[pos++] = 0;
		ret[pos++] = 0;

		ret[pos++] = 0x1; // transparent

		// text color
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;

		// xy set 1
		ret[pos++] = 0x20;
		ret[pos++] = 0;
		ret[pos++] = 0x60;
		ret[pos++] = 0;

		// xy set 2
		ret[pos++] = 0x10;
		ret[pos++] = 0x1;
		ret[pos++] = 0x90;
		ret[pos++] = 0;

		ret[pos++] = 0xf; // show 4 lines
		ret[pos++] = 1; // 1 messages follow
		ret[pos++] = 0; 
		ret[pos++] = 40;
		ret[pos++] = 0; 

		// another font
		ret[pos++] = 0x10;
		ret[pos++] = 0x10;
		ret[pos++] = 0x0;
		ret[pos++] = 0x0;
		ret[pos++] = 0x0;
		ret[pos++] = 0x3;

		// text rgb
		ret[pos++] = 0x0;
		ret[pos++] = 0x0;
		ret[pos++] = 0xff;

		ret[pos++] = 0x1; // transparent

		// background rgb
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;
		ret[pos++] = 0xff;

		// text xy
		ret[pos++] = 0x40;
		ret[pos++] = 0x0;
		ret[pos++] = 0x20;
		ret[pos++] = 0x0;

		string msg = "Enter Security Code";
		ret[pos++] = 19;
		ret[pos++] = 0;

		foreach(byte b in System.Text.Encoding.ASCII.GetBytes(msg)){
			ret[pos] = b;
			pos++;
		}

		return ret;
	}

	private byte[] ResetDevice(){
		return new byte[7]{0x78, 0x46, 0x0a, 0x49, 0x52, 0x46, 0x57};
	}

	private byte[] EnableAudio(){
		return new byte[4]{0x7b, 0x46, 0x1, 0x1};
	}
	
	private byte[] DoBeep(){
		return new byte[7]{0x7b, 0x46, 0x02, 0xff, 0x0, 0xff, 0};
	}

	/*
	private byte[] GetAmount(){
		byte[] ret = new byte[];
		int pos = 0;

		// command head
		ret[pos++] = 0x75;
		ret[pos++] = 0x46;
		ret[pos++] = 0x23;
		
		// min length
		ret[pos++] = 1;
		// max length
		ret[pos++] = 2;

		// serious manual translation breakdown occurs here
	}
	*/
}

}
