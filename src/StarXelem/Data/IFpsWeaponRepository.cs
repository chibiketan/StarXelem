namespace StarXelem.Data;

public interface IFpsWeaponRepository
{
    /// <summary>
    /// Retourne toutes les armes FPS (une ligne par modèle, variantes de livrée regroupées),
    /// fabricant inclus. Liste bornée à quelques dizaines d'entrées : le filtrage est fait
    /// en mémoire par le ViewModel.
    /// </summary>
    Task<List<FpsWeaponEntity>> GetAllAsync();
}
