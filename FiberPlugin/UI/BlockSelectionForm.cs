using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FiberPlugin.UI
{
    public class BlockSelectionForm : Form
    {
        private ComboBox cmbCategories;
        private ComboBox cmbBlocks;
        private Button btnOk;
        public string SelectedBlock { get; private set; }

        private Dictionary<string, List<string>> categorizedBlocks;

        public BlockSelectionForm(List<string> blockNames)
        {
            this.Text = "Fiber Plugin - Inserir Bloco";
            this.Size = new Size(350, 220);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // Lógica de categorização
            categorizedBlocks = new Dictionary<string, List<string>>();
            categorizedBlocks.Add("Fibra", new List<string>());
            categorizedBlocks.Add("Elétrica", new List<string>());
            categorizedBlocks.Add("Poste", new List<string>());
            categorizedBlocks.Add("Outros", new List<string>());

            // Define known blocks (lowercased for matching)
            var fibraSet = new HashSet<string> { "cto", "amarração", "amarracao" };
            var eletricaSet = new HashSet<string> { "aterramento", "chave ch", "chave fu", "para-raio", "trafo", "trafo com chave fu" };
            var posteSet = new HashSet<string> { "poste" };

            foreach (var name in blockNames)
            {
                string lowerName = name.ToLower();
                if (fibraSet.Contains(lowerName))
                {
                    categorizedBlocks["Fibra"].Add(name);
                }
                else if (eletricaSet.Contains(lowerName))
                {
                    categorizedBlocks["Elétrica"].Add(name);
                }
                else if (posteSet.Contains(lowerName))
                {
                    categorizedBlocks["Poste"].Add(name);
                }
                else
                {
                    categorizedBlocks["Outros"].Add(name);
                }
            }

            // UI Elements
            Label lblCat = new Label();
            lblCat.Text = "1. Categoria:";
            lblCat.Location = new Point(15, 15);
            lblCat.AutoSize = true;
            this.Controls.Add(lblCat);

            cmbCategories = new ComboBox();
            cmbCategories.Location = new Point(15, 35);
            cmbCategories.Size = new Size(300, 25);
            cmbCategories.DropDownStyle = ComboBoxStyle.DropDownList;
            
            // Adiciona apenas as categorias que não estão vazias
            foreach (var kvp in categorizedBlocks)
            {
                if (kvp.Value.Count > 0)
                {
                    cmbCategories.Items.Add(kvp.Key);
                }
            }

            this.Controls.Add(cmbCategories);

            Label lblBlock = new Label();
            lblBlock.Text = "2. Bloco a Inserir:";
            lblBlock.Location = new Point(15, 75);
            lblBlock.AutoSize = true;
            this.Controls.Add(lblBlock);

            cmbBlocks = new ComboBox();
            cmbBlocks.Location = new Point(15, 95);
            cmbBlocks.Size = new Size(300, 25);
            cmbBlocks.DropDownStyle = ComboBoxStyle.DropDownList;
            this.Controls.Add(cmbBlocks);

            // Atualiza blocos ao mudar categoria
            cmbCategories.SelectedIndexChanged += (s, e) =>
            {
                string selCat = cmbCategories.SelectedItem?.ToString();
                if (string.IsNullOrEmpty(selCat)) return;

                cmbBlocks.Items.Clear();
                foreach (string bName in categorizedBlocks[selCat])
                {
                    cmbBlocks.Items.Add(bName);
                }
                if (cmbBlocks.Items.Count > 0)
                    cmbBlocks.SelectedIndex = 0;
            };

            // Dispara a primeira atualização para preencher o cmbBlocks se houver itens
            if (cmbCategories.Items.Count > 0)
            {
                cmbCategories.SelectedIndex = 0;
            }

            btnOk = new Button();
            btnOk.Text = "INSERIR";
            btnOk.Location = new Point(235, 135);
            btnOk.Size = new Size(80, 30);
            btnOk.DialogResult = DialogResult.OK;
            btnOk.Click += (s, e) => { 
                SelectedBlock = cmbBlocks.SelectedItem?.ToString(); 
                if (!string.IsNullOrEmpty(SelectedBlock))
                {
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
            };
            this.Controls.Add(btnOk);

            this.AcceptButton = btnOk;
        }
    }
}
