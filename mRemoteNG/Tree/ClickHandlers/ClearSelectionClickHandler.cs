using System;
using System.Runtime.Versioning;
using mRemoteNG.Connection;
using mRemoteNG.UI.Controls.ConnectionTree;

namespace mRemoteNG.Tree.ClickHandlers
{
    /// <summary>
    /// sysprep fork: after a connection has been opened from the tree, nothing stays selected there -
    /// the tab shows which server is in front.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class ClearSelectionClickHandler : ITreeNodeClickHandler<ConnectionInfo>
    {
        private readonly ConnectionTree _connectionTree;

        public ClearSelectionClickHandler(ConnectionTree connectionTree)
        {
            _connectionTree = connectionTree ?? throw new ArgumentNullException(nameof(connectionTree));
        }

        public void Execute(ConnectionInfo clickedNode)
        {
            if (clickedNode == null) return;
            if (clickedNode.GetTreeNodeType() != TreeNodeType.Connection &&
                clickedNode.GetTreeNodeType() != TreeNodeType.PuttySession) return;

            // Only once the tree has finished handling the mouse click, or it selects the node again
            _connectionTree.BeginInvoke(() =>
            {
                if (!_connectionTree.IsDisposed)
                    _connectionTree.SelectedObject = null;
            });
        }
    }
}
