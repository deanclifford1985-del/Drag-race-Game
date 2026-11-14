namespace DragRace.Simulation
{
    /// <summary>Minimal persistent storage. The Unity layer implements this over PlayerPrefs.</summary>
    public interface IKeyValueStore
    {
        int GetInt(string key, int fallback);
        void SetInt(string key, int value);
        string GetString(string key, string fallback);
        void SetString(string key, string value);
        void Save();
    }

    /// <summary>Player cash, saved locally. Each race id can be paid out at most once.</summary>
    public class Wallet
    {
        const string CashKey = "wallet.cash";
        const string LastPaidKey = "wallet.lastPaidRace";

        readonly IKeyValueStore store;

        public Wallet(IKeyValueStore store) { this.store = store; }

        public int Cash { get { return store.GetInt(CashKey, 0); } }

        /// <summary>Pays the prize for a race. Returns false if that race was already paid.</summary>
        public bool TryAward(string raceId, int amount)
        {
            if (store.GetString(LastPaidKey, "") == raceId) return false;
            store.SetInt(CashKey, Cash + amount);
            store.SetString(LastPaidKey, raceId);
            store.Save();
            return true;
        }
    }
}
