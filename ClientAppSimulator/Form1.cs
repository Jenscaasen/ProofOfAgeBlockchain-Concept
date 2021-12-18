using Node;
using System.Security.Cryptography;
using System.Text.Json;

namespace ClientAppSimulator
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void btnAddAccount_Click(object sender, EventArgs e)
        {
            RSA rsa = RSA.Create();
            string privateKey = rsa.ToXmlString(true);
            string publicKey = rsa.ToXmlString(false);

            accounts.Add(new Account { PrivateKey = privateKey, PublicKey = publicKey });
            File.WriteAllText("accounts.json", JsonSerializer.Serialize(accounts));


            LOadAccounts();

        }

        List<Account> accounts = new List<Account>();
        private void Form1_Load(object sender, EventArgs e)
        {
            if (File.Exists("accounts.json"))
            {
                LOadAccounts();

            }
        }

        private void LOadAccounts()
        {
            string accountsJson = File.ReadAllText("accounts.json");
            List<Account> accs = JsonSerializer.Deserialize<List<Account>>(accountsJson);
            accounts = accs;
            cmbAccounts.Items.Clear();
            cmbSendTo.Items.Clear();
            foreach (var acc in accounts)
            {
                cmbAccounts.Items.Add(acc);
                cmbSendTo.Items.Add(acc);
            }
            cmbAccounts.SelectedIndex = 0;
            cmbSendTo.SelectedIndex = 0;
        }

      
        private void cmbAccounts_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private async void btnRefreshBalance_Click(object sender, EventArgs e)
        {
            Account account = cmbAccounts.SelectedItem as Account;
            HttpClient http = new HttpClient()
            {
                DefaultRequestVersion = new Version(2, 0)
            };

            var walletId = HashHelper.GetHash(account.PublicKey);
            var postUrí = new Uri(txtNodeUrl.Text + "/getwallet/"+walletId);

            var response = await http.GetAsync(postUrí);
         string data = await   response.Content.ReadAsStringAsync();
            lblStatus.Text = response.StatusCode.ToString();
            WalletData walletData = JsonSerializer.Deserialize<WalletData>(data);
            lblBalance.Text = walletData.Amount.ToString();
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            Account accountFrom =  cmbAccounts.SelectedItem as Account;
            Account accountTo = cmbSendTo.SelectedItem as Account;

            string from = HashHelper.GetHash(accountFrom.PublicKey);
            string to = HashHelper.GetHash(accountTo.PublicKey);

            UserSubmittedTransaction transactionPackage = new UserSubmittedTransaction();
            Transaction transaction = new Transaction { Amount = double.Parse(txtSendAmount.Text), Receiver = to, Sender = from };
            transactionPackage.Transaction = transaction;

            transactionPackage.publicKey = accountFrom.PublicKey;
            var signature = HashHelper.MakeSignature(transaction, accountFrom.PrivateKey);
            transactionPackage.SignatureBase64 = System.Convert.ToBase64String(signature);

            string packageJson = JsonSerializer.Serialize(transactionPackage);

            HttpClient http = new HttpClient()
            {
                DefaultRequestVersion = new Version(2, 0)
            };

            var postUrí = new Uri(txtNodeUrl.Text + "/transaction");
            
            var objTest = JsonSerializer.Deserialize<UserSubmittedTransaction>(packageJson);
            byte[] signatureDeserialized = System.Convert.FromBase64String(objTest.SignatureBase64);
         bool check =   HashHelper.CheckSignature(objTest.Transaction, signatureDeserialized, objTest.publicKey);

            
            HttpContent content = new StringContent(packageJson);
         var response =  await http.PostAsync(postUrí, content);

            lblStatus.Text = response.StatusCode.ToString();
        }

        class Account
        {

            public override string ToString()
            {
                return HashHelper.GetHash(PublicKey);
            }
            public string PublicKey { get; set; }
            public string PrivateKey { get; set; }
        }
      public  class Transaction
        {
            public string Sender { get; set; }
            public string Receiver { get; set; }
            public double Amount { get; set; }
        }
        class UserSubmittedTransaction
        {
            public string publicKey { get; set; }
            public string SignatureBase64 { get; set; }
            public Transaction Transaction { get; set; }
        }

        public class WalletData
        {
            public string WalletId { get; set; }
            public double Amount { get; set; }
            public List<Transaction> TransactionList { get; set; }
        }
    }
}