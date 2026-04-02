using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FiberPlugin.UI
{
    public class CommandOption
    {
        public string DislayName { get; set; }
        public string CommandName { get; set; }
        
        public override string ToString()
        {
            return DislayName;
        }
    }

    public class MainMenuForm : Form
    {
        private ListBox lstCommands;
        private Button btnExecute;
        
        public string SelectedCommandName { get; private set; }

        public MainMenuForm()
        {
            this.Text = "Menu Principal - Fiber Plugin";
            this.Size = new Size(500, 380);
            this.MinimumSize = new Size(400, 300);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable; // Janeça responsiva (redimensionável)
            this.MaximizeBox = true;
            this.MinimizeBox = false;

            // Aumenta o texto global do formulário para todas as listboxes e botões
            this.Font = new Font("Segoe UI", 12f, FontStyle.Regular); 

            Label lbl = new Label();
            lbl.Text = "Selecione a ferramenta que deseja usar:";
            lbl.Location = new Point(15, 15);
            lbl.AutoSize = true;
            this.Controls.Add(lbl);

            lstCommands = new ListBox();
            lstCommands.Location = new Point(15, 45);
            // Tamanho inicial grande com espaçamento extra
            lstCommands.Size = new Size(450, 230); 
            // Faz com que a lista se expanda quando a tela esticar (Responsive)
            lstCommands.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lstCommands.ItemHeight = 25; // Garante mais entrelinha entre os itens
            
            // Popula as opções (comando Esforço 1 Cabo foi removido)
            var options = new List<CommandOption>
            {
                new CommandOption { DislayName = "1. Lançar Rota Manual de Cabo", CommandName = "FIBRA_LANCAR_CABO" },
                new CommandOption { DislayName = "2. Roteamento Automático de Cabo", CommandName = "FIBRA_ROTEAMENTO_AUTO" },
                new CommandOption { DislayName = "3. Inserir Postes Sequenciais", CommandName = "FIBRA_INSERIR_POSTE" },
                new CommandOption { DislayName = "4. Inserir Blocos Genéricos", CommandName = "FIBRA_INSERIR_BLOCO" },
                new CommandOption { DislayName = "5. Nomear Atributos de Postes", CommandName = "FIBRA_NOMEAR_POSTE" },
                new CommandOption { DislayName = "6. Numerar Pontos (Georreferenciamento)", CommandName = "FIBRA_NUMERAR_PONTOS" },
                new CommandOption { DislayName = "7. Calcular Esforço Resultante Total nos Postes", CommandName = "FIBRA_ESFORCO_TOTAL" },
                new CommandOption { DislayName = "8. Exportar Lista de Materiais e Coordenadas (.CSV)", CommandName = "FIBRA_EXPORTAR_CSV" },
                new CommandOption { DislayName = "9. Calcular Bobinas de Cabo", CommandName = "FIBRA_CALCULAR_BOBINAS" },
                new CommandOption { DislayName = "10. Exportar Relatório de Esforços dos Postes (.CSV)", CommandName = "FIBRA_RELATORIO_ESFORCOS" }
            };

            foreach (var opt in options)
            {
                lstCommands.Items.Add(opt);
            }
            if (lstCommands.Items.Count > 0) lstCommands.SelectedIndex = 0;

            // Executa duplo-clique para abrir mais rápido
            lstCommands.DoubleClick += (s, e) => {
                if (lstCommands.SelectedItem != null)
                {
                    SelectedCommandName = ((CommandOption)lstCommands.SelectedItem).CommandName;
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
            };

            this.Controls.Add(lstCommands);

            btnExecute = new Button();
            btnExecute.Text = "EXECUTAR";
            // Posiciona no canto inferior direito
            btnExecute.Location = new Point(345, 290);
            btnExecute.Size = new Size(120, 35);
            // Faz o botão se ancorar pra sempre ficar grudado no canto ao maximizar a tela
            btnExecute.Anchor = AnchorStyles.Bottom | AnchorStyles.Right; 

            btnExecute.Click += (s, e) => {
                if (lstCommands.SelectedItem != null)
                {
                    SelectedCommandName = ((CommandOption)lstCommands.SelectedItem).CommandName;
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
            };
            this.Controls.Add(btnExecute);
            
            this.AcceptButton = btnExecute;
        }
    }
}
