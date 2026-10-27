using UnityEngine;

namespace Ashveil.Data
{
    /// <summary>Haengt in der Szene und macht die Datenbank verfuegbar, bevor irgendetwas anderes startet.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameDataBootstrap : MonoBehaviour
    {
        [SerializeField] private DefinitionDatabase database;
        [Tooltip("Klasse neuer Charaktere (bis zum Charaktereditor)")]
        [SerializeField] private CharacterClassDefinition defaultClass;
        [Tooltip("Waffe, wenn keine angelegt ist")]
        [SerializeField] private WeaponDefinition unarmed;
        [Tooltip("Frisuren, Hauttoene, Paletten, Materialvorlage")]
        [SerializeField] private AppearanceCatalog appearanceCatalog;

        public static CharacterClassDefinition DefaultClass { get; private set; }
        public static WeaponDefinition Unarmed { get; private set; }
        public static AppearanceCatalog Catalog { get; private set; }

        private void Awake()
        {
            GameData.Register(database);
            if (defaultClass != null) DefaultClass = defaultClass;
            if (unarmed != null) Unarmed = unarmed;
            if (appearanceCatalog != null) Catalog = appearanceCatalog;
        }
    }
}
