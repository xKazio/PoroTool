using System.Drawing;
using System.Windows.Forms;

namespace PoroTool
{
    /// <summary>
    /// The app's light palette and the styling applied to controls.
    /// Colors come from the poro itself: white fur, warm horn browns,
    /// pink tongue as the accent. Kept in one place so the Designer
    /// file stays layout-only.
    /// </summary>
    static class Theme
    {
        public static readonly Color Sidebar = ColorTranslator.FromHtml("#F1EBE1");
        public static readonly Color Canvas = ColorTranslator.FromHtml("#FAF7F2");
        public static readonly Color Surface = ColorTranslator.FromHtml("#FFFFFF");
        public static readonly Color SurfaceHover = ColorTranslator.FromHtml("#F7F1E7");
        public static readonly Color SurfacePressed = ColorTranslator.FromHtml("#EBE2D3");
        public static readonly Color Hairline = ColorTranslator.FromHtml("#D9CDBB");
        public static readonly Color TextPrimary = ColorTranslator.FromHtml("#40342A");
        public static readonly Color TextMuted = ColorTranslator.FromHtml("#94836E");
        public static readonly Color Pink = ColorTranslator.FromHtml("#C75B8B");
        public static readonly Color Success = ColorTranslator.FromHtml("#3E8E58");
        public static readonly Color Error = ColorTranslator.FromHtml("#C44F3F");

        // Official brand colors for the social link buttons.
        public static readonly Color DiscordBrand = ColorTranslator.FromHtml("#5865F2");
        public static readonly Color DiscordBrandHover = ColorTranslator.FromHtml("#6D78F4");
        public static readonly Color GitHubBrand = ColorTranslator.FromHtml("#24292E");
        public static readonly Color GitHubBrandHover = ColorTranslator.FromHtml("#3A4046");

        public static readonly Font ButtonFont = new Font("Segoe UI", 9.75f);
        public static readonly Font PrimaryButtonFont = new Font("Segoe UI Semibold", 10f);
        public static readonly Font SectionFont = new Font("Segoe UI Semibold", 8.25f);
        public static readonly Font StatusFont = new Font("Segoe UI", 9f);
        public static readonly Font RowFont = new Font("Segoe UI", 9.75f);
        public static readonly Font RowHeaderFont = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font MonoFont = new Font("Consolas", 9.75f);

        public static void StyleButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Surface;
            button.ForeColor = TextPrimary;
            button.Font = ButtonFont;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Hairline;
            button.FlatAppearance.MouseOverBackColor = SurfaceHover;
            button.FlatAppearance.MouseDownBackColor = SurfacePressed;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(10, 0, 0, 0);
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
        }

        public static void StylePrimaryButton(Button button)
        {
            StyleButton(button);
            button.Font = PrimaryButtonFont;
            button.ForeColor = Pink;
            button.FlatAppearance.BorderColor = Pink;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Padding = Padding.Empty;
        }

        public static void StyleTextButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Sidebar;
            button.ForeColor = TextMuted;
            button.Font = SectionFont;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = SurfacePressed;
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
        }

        public static void StyleComboBox(ComboBox combo)
        {
            combo.FlatStyle = FlatStyle.Flat;
            combo.Font = RowFont;
            combo.BackColor = Surface;
            combo.ForeColor = TextPrimary;

            // Draw the dropdown items ourselves so they match the theme.
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.DrawItem += DrawComboBoxItem;
        }

        private static void DrawComboBoxItem(object sender, DrawItemEventArgs e)
        {
            var combo = (ComboBox)sender;
            bool selected = (e.State & DrawItemState.Selected) != 0;

            using (var background = new SolidBrush(selected ? SurfaceHover : Surface))
                e.Graphics.FillRectangle(background, e.Bounds);

            if (e.Index >= 0)
            {
                string text = combo.GetItemText(combo.Items[e.Index]);
                TextRenderer.DrawText(e.Graphics, text, combo.Font, e.Bounds, TextPrimary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
        }

        public static void StyleNumeric(NumericUpDown numeric)
        {
            numeric.Font = RowFont;
            numeric.BackColor = Surface;
            numeric.ForeColor = TextPrimary;
            numeric.BorderStyle = BorderStyle.FixedSingle;
        }

        public static void StyleSocialButton(Button button, Color brand, Color brandHover, Image icon)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = brand;
            button.ForeColor = Color.White;
            button.Font = ButtonFont;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = brandHover;
            button.FlatAppearance.MouseDownBackColor = brand;
            button.Image = icon;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.ImageAlign = ContentAlignment.MiddleLeft;
            button.TextAlign = ContentAlignment.MiddleRight;
            button.Padding = new Padding(8, 0, 8, 0);
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
        }

        public static void StyleTextBox(TextBox textBox)
        {
            textBox.Font = RowFont;
            textBox.BackColor = Surface;
            textBox.ForeColor = TextPrimary;
            textBox.BorderStyle = BorderStyle.FixedSingle;
        }

        public static void StyleCheckBox(CheckBox checkBox)
        {
            checkBox.Font = RowFont;
            checkBox.ForeColor = TextPrimary;
            checkBox.BackColor = Canvas;
            checkBox.FlatStyle = FlatStyle.Flat;
            checkBox.FlatAppearance.BorderColor = Hairline;
            checkBox.AutoSize = true;
            checkBox.Cursor = Cursors.Hand;
        }

        public static void StyleListView(ListView listView)
        {
            listView.Font = RowFont;
            listView.BackColor = Surface;
            listView.ForeColor = TextPrimary;
            listView.BorderStyle = BorderStyle.FixedSingle;
        }
    }
}
