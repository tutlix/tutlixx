// Sign in / create account / two-factor - same server calls as the PC
// (player_verify, player_create, email_send_code), with "remember this
// device" auto-login through the device token.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FortBox
{
    public class LoginScreen : UiScreen
    {
        RectTransform signIn, register, totpBox, busyBox, serverBox;
        InputField user, pass, totp, rUser, rName, rPass, rPass2, rBirth, rEmail, rCode, host;
        Toggle remember;
        Text status, busyText;
        Ui.Btn tabIn, tabUp;
        bool busy, triedAuto;
        string pendingUser, pendingPass;

        public override void Build()
        {
            // brand side
            var brand = Ui.Node(root, "brand").Place(0, 0, 0.5f, 1, 80, 80, 40, 80);
            Ui.Img(brand, Ui.Orange, null, "bar").rectTransform.Box(0, 0.5f, 0, 70, 8, 90);
            var w = Ui.Label(brand, "FORTBOX", 92, Ui.Text, TextAnchor.MiddleLeft, Ui.Bold);
            w.rectTransform.Box(0, 0.5f, 26, 82, 520, 110);
            var d3 = Ui.Label(brand, "3D", 92, Ui.Orange, TextAnchor.MiddleLeft, Ui.Bold);
            d3.rectTransform.Box(0, 0.5f, 400, 82, 200, 110);
            Ui.Label(brand, "TACTICAL SHOOTER", 22, Ui.Text3, TextAnchor.MiddleLeft, Ui.Bold).rectTransform.Box(0, 0.5f, 30, 16, 520, 30);
            Ui.Label(brand, "Same account, same servers, same coins and weapons as the PC game.", 20, Ui.Text2, TextAnchor.UpperLeft, Ui.Regular)
                .rectTransform.Box(0, 0.5f, 30, -30, 560, 70);

            var card = Ui.Panel(root, 0.95f, Ui.Orange);
            card.Place(0.5f, 0, 1, 1, 40, 60, 80, 60);
            var tabs = Ui.Node(card, "tabs").Place(0, 1, 1, 1, 28, -80, 28, 24);
            tabs.HList(10, 0, true);
            tabIn = Ui.Button(tabs, "SIGN IN", Ui.Style.Primary, "user", () => ShowTab(true), 18);
            tabIn.GetComponent<RectTransform>().Size(200, -1);
            tabUp = Ui.Button(tabs, "CREATE ACCOUNT", Ui.Style.Ghost, "plus", () => ShowTab(false), 18);
            tabUp.GetComponent<RectTransform>().Size(260, -1);
            var gear = Ui.Button(card, "", Ui.Style.Ghost, "gear", () => serverBox.gameObject.SetActive(!serverBox.gameObject.activeSelf), 22);
            gear.GetComponent<RectTransform>().Box(1, 1, -20, -22, 52, 52);

            // sign in
            signIn = Ui.Node(card, "signin").Place(0, 0, 1, 1, 28, 90, 28, 100);
            signIn.VList(14, 0);
            Ui.Section(signIn, "Welcome back");
            user = Ui.Input(signIn, "Username");
            user.GetComponent<RectTransform>().Size(-1, 62);
            user.text = PlayerPrefs.GetString("fb_user", "");
            pass = Ui.Input(signIn, "Password", true);
            pass.GetComponent<RectTransform>().Size(-1, 62);
            remember = Ui.Check(signIn, "Remember this device", true, null);
            Ui.Button(signIn, "SIGN IN", Ui.Style.Primary, "play", DoLogin, 22).GetComponent<RectTransform>().Size(-1, 70);

            // register
            register = Ui.Node(card, "register").Place(0, 0, 1, 1, 28, 90, 28, 100);
            ScrollRect sr;
            var rc = Ui.Scroll(register, out sr, 10);
            sr.GetComponent<RectTransform>().Fill();
            Ui.Section(rc, "New player");
            rUser = Field(rc, "Username (3-16 letters, numbers, _)");
            rName = Field(rc, "Display name (optional)");
            rPass = Field(rc, "Password (8+ characters)", true);
            rPass2 = Field(rc, "Repeat password", true);
            rBirth = Field(rc, "Date of birth (YYYY-MM-DD)");
            rEmail = Field(rc, "Email");
            rEmail.contentType = InputField.ContentType.EmailAddress;
            var codeRow = Ui.Node(rc, "coderow");
            codeRow.Size(-1, 58);
            codeRow.HList(10, 0, true);
            rCode = Ui.Input(codeRow, "Email code");
            rCode.GetComponent<RectTransform>().Size(-1, -1, 1);
            Ui.Button(codeRow, "SEND CODE", Ui.Style.Secondary, "bell", DoSendCode, 16).GetComponent<RectTransform>().Size(190, -1);
            Ui.Button(rc, "CREATE ACCOUNT", Ui.Style.Primary, "check", DoRegister, 22).GetComponent<RectTransform>().Size(-1, 68);
            register.gameObject.SetActive(false);

            // two-factor
            totpBox = Ui.Node(card, "totp").Place(0, 0, 1, 1, 28, 90, 28, 100);
            totpBox.VList(14, 0);
            Ui.Section(totpBox, "Two-factor authentication");
            Ui.Label(totpBox, "Enter the 6-digit code from your authenticator app.", 20, Ui.Text2).rectTransform.Size(-1, 40);
            totp = Ui.Input(totpBox, "123456");
            totp.contentType = InputField.ContentType.IntegerNumber;
            totp.characterLimit = 8;
            totp.GetComponent<RectTransform>().Size(-1, 62);
            Ui.Button(totpBox, "VERIFY", Ui.Style.Primary, "shield", DoTotp, 22).GetComponent<RectTransform>().Size(-1, 70);
            Ui.Button(totpBox, "BACK", Ui.Style.Ghost, "back", () => { totpBox.gameObject.SetActive(false); ShowTab(true); }, 18).GetComponent<RectTransform>().Size(-1, 50);
            totpBox.gameObject.SetActive(false);

            status = Ui.Label(card, "", 18, Ui.Amber, TextAnchor.MiddleCenter, Ui.Semi);
            status.rectTransform.Place(0, 0, 1, 0, 28, 24, 28, -70);

            // server address (for test servers)
            serverBox = Ui.Panel(card, 0.99f, Ui.Amber);
            serverBox.Box(1, 1, -20, -84, 420, 170);
            var sb = Ui.Node(serverBox, "col").Fill(16);
            sb.VList(10, 0);
            Ui.Section(sb, "Game server");
            host = Ui.Input(sb, "host:port");
            host.text = Config.Host;
            host.GetComponent<RectTransform>().Size(-1, 52);
            Ui.Button(sb, "SAVE", Ui.Style.Secondary, "save", () =>
            {
                string h = host.text.Trim();
                if (h.Length > 0) { PlayerPrefs.SetString("fb_host", h); Config.Host = h; Push.Stop(); Push.Start(); Toast.Show("Server set to " + h); }
                serverBox.gameObject.SetActive(false);
            }, 16).GetComponent<RectTransform>().Size(-1, 46);
            serverBox.gameObject.SetActive(false);

            busyBox = Ui.Node(root, "busy").Fill();
            Ui.Img(busyBox, new Color(0.03f, 0.035f, 0.045f, 0.75f), null, "dim").raycastTarget = true;
            busyBox.GetChild(0).GetComponent<RectTransform>().Fill();
            busyText = Ui.Label(busyBox, "SIGNING IN...", 30, Ui.Text, TextAnchor.MiddleCenter, Ui.Bold);
            busyText.rectTransform.Fill();
            busyBox.gameObject.SetActive(false);
        }

        static InputField Field(Transform parent, string ph, bool pw = false)
        {
            var f = Ui.Input(parent, ph, pw);
            f.GetComponent<RectTransform>().Size(-1, 58);
            return f;
        }

        void ShowTab(bool inTab)
        {
            signIn.gameObject.SetActive(inTab);
            register.gameObject.SetActive(!inTab);
            totpBox.gameObject.SetActive(false);
            tabIn.style = inTab ? Ui.Style.Primary : Ui.Style.Ghost; tabIn.Refresh();
            tabUp.style = inTab ? Ui.Style.Ghost : Ui.Style.Primary; tabUp.Refresh();
            status.text = "";
        }

        public override void OnShow()
        {
            App.I.SetBackdrop(Settings.LastMap);
            if (!triedAuto)
            {
                triedAuto = true;
                if (!string.IsNullOrEmpty(PlayerPrefs.GetString("fb_device", ""))) AutoLogin();
            }
        }

        void Busy(bool on, string text = "SIGNING IN...")
        {
            busy = on;
            busyText.text = text;
            busyBox.gameObject.SetActive(on);
            busyBox.SetAsLastSibling();
        }

        async void AutoLogin()
        {
            Busy(true);
            var r = await Session.AutoLogin();
            Busy(false);
            if (r.ok) Enter();
            else if (r.totp) { pendingUser = PlayerPrefs.GetString("fb_user", ""); pendingPass = Session.DevicePrefix + PlayerPrefs.GetString("fb_device", ""); ShowTotp(r.msg); }
            else if (!string.IsNullOrEmpty(r.msg)) status.text = r.msg;
        }

        async void DoLogin()
        {
            if (busy) return;
            status.text = "";
            Busy(true);
            pendingUser = user.text.Trim();
            pendingPass = pass.text;
            var r = await Session.Login(pendingUser, pendingPass, remember.isOn);
            Busy(false);
            if (r.ok) Enter();
            else if (r.totp) ShowTotp(r.msg);
            else { status.text = r.msg; status.color = r.banned ? Ui.Red : Ui.Amber; }
        }

        void ShowTotp(string msg)
        {
            signIn.gameObject.SetActive(false);
            register.gameObject.SetActive(false);
            totpBox.gameObject.SetActive(true);
            status.text = msg;
            status.color = Ui.Amber;
        }

        async void DoTotp()
        {
            if (busy) return;
            Busy(true);
            var r = await Session.Login(pendingUser, pendingPass, remember.isOn, totp.text.Trim());
            Busy(false);
            if (r.ok) Enter();
            else { status.text = r.msg; status.color = Ui.Red; }
        }

        async void DoSendCode()
        {
            string em = rEmail.text.Trim();
            if (em.IndexOf('@') < 1) { status.text = "Enter your email first"; return; }
            Busy(true, "SENDING CODE...");
            bool ok = await Session.SendEmailCode(em, rUser.text.Trim());
            Busy(false);
            status.text = ok ? "Code sent - check your inbox" : "Could not send the code";
            status.color = ok ? Ui.Green : Ui.Red;
        }

        async void DoRegister()
        {
            if (busy) return;
            string u = rUser.text.Trim();
            if (u.Length < 3) { status.text = "Username too short"; return; }
            if (rPass.text.Length < 8) { status.text = "Password must be at least 8 characters"; return; }
            if (rPass.text != rPass2.text) { status.text = "Passwords don't match"; return; }
            System.DateTime bd;
            if (!System.DateTime.TryParseExact(rBirth.text.Trim(), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out bd))
            { status.text = "Date of birth must look like 2008-04-21"; return; }
            Busy(true, "CREATING ACCOUNT...");
            var r = await Session.Register(u, rPass.text, rBirth.text.Trim(), rName.text.Trim(), rEmail.text.Trim(), rCode.text.Trim());
            if (!r.ok) { Busy(false); status.text = r.msg; status.color = Ui.Red; return; }
            var l = await Session.Login(u, rPass.text, true);
            Busy(false);
            if (l.ok) { Toast.Show("Welcome to FortBox, " + u + "!", Ui.Green); Enter(); }
            else { ShowTab(true); user.text = u; status.text = "Account created - sign in"; status.color = Ui.Green; }
        }

        void Enter()
        {
            pass.text = "";
            totp.text = "";
            Push.TryIdentify();
            App.ClearHistory();
            App.Show<MenuScreen>(false);
        }

        public override bool OnBack()
        {
            if (totpBox.gameObject.activeSelf) { ShowTab(true); return true; }
            Application.Quit();
            return true;
        }
    }
}
