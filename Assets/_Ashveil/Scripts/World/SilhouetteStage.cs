using System.Collections.Generic;
using UnityEngine;

namespace Ashveil.World
{
    /// <summary>
    /// Art-Pass 4/5: Silhouetten-Buehne – Referenzfiguren (Basiskoerper, Cael, Elara, Seris, Quaternius zum Vergleich)
    /// in einer Reihe. Standardmaessig ausgeblendet; der Silhouettentest schaltet sie ein. Rein visuell, kein Netz.
    /// </summary>
    public sealed class SilhouetteStage : MonoBehaviour
    {
        [SerializeField] private GameObject figuresRoot;
        [SerializeField] private List<Transform> figures = new List<Transform>();
        [SerializeField] private List<string> labels = new List<string>();

        public IReadOnlyList<Transform> Figures => figures;
        public IReadOnlyList<string> Labels => labels;
        public bool Visible => figuresRoot != null && figuresRoot.activeSelf;

        public void Configure(GameObject root, List<Transform> list, List<string> names)
        {
            figuresRoot = root;
            figures = list;
            labels = names;
        }

        public void Show(bool on)
        {
            if (figuresRoot != null) figuresRoot.SetActive(on);
        }
    }
}
