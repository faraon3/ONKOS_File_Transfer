void LstPeers_DrawItem(object sender, DrawItemEventArgs e)
{
    if (e.Index < 0 || e.Index >= _lstPeers.Items.Count)
        return;

    bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
    Color bg = selected ? Color.FromArgb(46, 38, 78) : Color.FromArgb(18, 20, 32);

    using (var brush = new SolidBrush(bg))
        e.Graphics.FillRectangle(brush, e.Bounds);

    if (_lstPeers.Items[e.Index] is PeerInfo peer)
    {
        bool online = PeerDiscoveryService.IsPeerOnline(peer);
        Color dotColor = online
            ? Color.FromArgb(34, 197, 94)   // Verde: disponible
            : Color.FromArgb(244, 63, 94); // Rojo: apagado / suspendido / sin conexión

        int dotSize = 8;
        int dotX = e.Bounds.X + 10;
        int dotY = e.Bounds.Y + (e.Bounds.Height - dotSize) / 2;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var dotBrush = new SolidBrush(dotColor))
        {
            e.Graphics.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);
        }

        string labelText = online
            ? peer.DisplayText
            : $"{peer.DisplayText}  •  No disponible";

        Color fg = !online
            ? Color.FromArgb(251, 113, 133)
            : (selected ? Color.White : Color.FromArgb(225, 229, 245));

        Rectangle textRect = new Rectangle(
            dotX + dotSize + 8,
            e.Bounds.Y + 4,
            Math.Max(20, e.Bounds.Width - (dotX + dotSize + 16)),
            Math.Max(16, e.Bounds.Height - 4));

        TextRenderer.DrawText(
            e.Graphics,
            labelText,
            new Font("Segoe UI", 9F, online && selected ? FontStyle.Bold : FontStyle.Regular),
            textRect,
            fg,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}
