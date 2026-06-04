using VPet_Simulator.Windows.Interface;

namespace TestPlugin
{
    public class Main : MainPlugin
    {
        public override string PluginName => "TestPlugin";
        public Main(IMainWindow mainwin) : base(mainwin) { }
        public override void LoadPlugin() { }
        public override void GameLoaded() { }
        public override void LoadDIY() { }
        public override void Setting() { }
        public override void Save() { }
        public override void EndGame() { }
    }
}
