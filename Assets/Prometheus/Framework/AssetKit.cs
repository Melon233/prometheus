namespace Xuan.Prometheus
{
    public interface IAssetKit
    {
        void LoadAssetSync(string location);
        void LoadSceneSync(string location);
        void InstantiateAsset(string location);
    }
    public class AssetKit : Kit, IAssetKit
    {
        public void InstantiateAsset(string location)
        {
            throw new System.NotImplementedException();
        }

        public void LoadAssetSync(string location)
        {
            throw new System.NotImplementedException();
        }

        public void LoadSceneSync(string location)
        {
            throw new System.NotImplementedException();
        }
    }
}