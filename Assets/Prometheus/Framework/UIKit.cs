using System;
using System.Collections.Generic;

namespace Xuan.Prometheus
{
    public interface IPanel
    {

    }
    public interface IUIKit
    {
        void OpenPanel<T>() where T : IPanel;
        void ClosePanel<T>() where T : IPanel;
    }
    public class UIKit : Kit, IUIKit
    {
        private Dictionary<Type, IPanel> panelDict = new Dictionary<Type, IPanel>();

        public void ClosePanel<T>() where T : IPanel
        {
        }

        public void OpenPanel<T>() where T : IPanel
        {
        }
    }
}