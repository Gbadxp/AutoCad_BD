using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FiberPlugin.UI
{
    public class CableSelectionForm : Form
    {
        private ComboBox cmbCables;
        private Button btnOk;
        public FiberPlugin.Models.CableModel SelectedCable { get; private set; }

        public CableSelectionForm(List<FiberPlugin.Models.CableModel> cables)
        {
            this.Text = "Fiber Plugin - Selecionar Cabo";
            this.Size = new Size(350, 160);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            Label lbl = new Label();
            lbl.Text = "Selecione o tipo de cabo para o traçado:";
            lbl.Location = new Point(15, 15);
            lbl.AutoSize = true;
            this.Controls.Add(lbl);

            cmbCables = new ComboBox();
            cmbCables.Location = new Point(15, 40);
            cmbCables.Size = new Size(300, 25);
            cmbCables.DropDownStyle = ComboBoxStyle.DropDownList;
            
            foreach (var cable in cables)
            {
                cmbCables.Items.Add(cable);
            }
            
            if (cmbCables.Items.Count > 0)
                cmbCables.SelectedIndex = 0;
                
            this.Controls.Add(cmbCables);

            btnOk = new Button();
            btnOk.Text = "LANÇAR";
            btnOk.Location = new Point(235, 75);
            btnOk.Size = new Size(80, 30);
            btnOk.DialogResult = DialogResult.OK;
            btnOk.Click += (s, e) => { 
                SelectedCable = cmbCables.SelectedItem as FiberPlugin.Models.CableModel; 
                this.DialogResult = DialogResult.OK;
                this.Close(); 
            };
            this.Controls.Add(btnOk);

            this.AcceptButton = btnOk;
        }
    }
}
