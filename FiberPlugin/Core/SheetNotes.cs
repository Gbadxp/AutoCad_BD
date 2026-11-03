namespace FiberPlugin.Core
{
    /// <summary>Os dois avisos da margem esquerda das folhas do Gerar Folhas (Lei 5.194/66 e propriedade dos desenhos).</summary>
    public static class SheetNotes
    {
        /// <summary>Aviso da margem esquerda, em cima (Art. 18 da Lei Federal 5.194/66).</summary>
        public const string ChangesNotice =
            "Conforme Art. 18 da Lei Federal 5.194/66: \"As alterações do projeto ou plano original só poderão ser feitas pelo " +
            "profissional que o tenha elaborado.\" Portanto, toda e qualquer alteração técnica deste projeto deve ser solicitada ao " +
            "Coordenador Responsável pela execução, o qual deverá aprovar com o projetista antes da sua realização.";

        /// <summary>Aviso da margem esquerda, embaixo (propriedade dos desenhos).</summary>
        public const string PropertyNotice =
            "Os desenhos nesta prancha são de nossa propriedade, sendo proibido cópias, ampliações, reproduções totais e parciais ou " +
            "ceder a terceiros, salvo com autorização expressa de acordo com a legislação brasileira em vigor.";
    }
}
