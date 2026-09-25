namespace AlchemyStars.Avalonia;

/// <summary>Module-owned live localization; host language and theme remain independent.</summary>
public sealed class ModelMergerText
{
    public int Language { get; set; }
    public static readonly string[] Languages = ["简体中文", "English", "Français", "Русский", "Español"];
    public string this[string key] => Catalog.TryGetValue(key, out var values) ? values[Math.Clamp(Language, 0, 4)] : key;
    private static readonly Dictionary<string, string[]> Catalog = Build();
    private static Dictionary<string, string[]> Build()
    {
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var line in Rows.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var values = line.Split('|');
            result.Add(values[0], values[1..]);
        }
        return result;
    }
    private const string Rows = """
title|模型合并|Model merger|Fusion de modèles|Объединение моделей|Fusión de modelos
new|新建模型组|New group|Nouveau groupe|Новая группа|Nuevo grupo
group|模型组|Group|Groupe|Группа|Grupo
preview|预览|Preview|Aperçu|Просмотр|Vista previa
openPreview|打开模型预览…|Open model preview…|Ouvrir un aperçu…|Открыть просмотр…|Abrir vista previa…
previewDrop|点击或拖入 CAST 文件，打开独立预览|Click or drop CAST files for independent previews|Cliquer ou déposer des fichiers CAST pour les afficher|Нажмите или перетащите CAST для просмотра|Pulse o arrastre archivos CAST para vistas independientes
hint|每组 2–15 个 CAST 部件；最多同时处理 2 个任务。原文件保持不变。|2–15 CAST parts per group; up to 2 concurrent tasks. Source files stay unchanged.|2–15 pièces CAST par groupe ; 2 tâches simultanées. Sources inchangées.|2–15 частей CAST в группе; до 2 задач одновременно. Исходники не меняются.|2–15 piezas CAST por grupo; hasta 2 tareas simultáneas. Las fuentes no cambian.
add|添加部件…|Add parts…|Ajouter des pièces…|Добавить части…|Añadir piezas…
replace|替换|Replace|Remplacer|Заменить|Reemplazar
remove|移除|Remove|Retirer|Удалить|Quitar
root|设为根|Set as root|Définir la racine|Сделать корнем|Establecer raíz
auto|自动识别根模型|Automatic root|Racine automatique|Автоматический корень|Raíz automática
manual|手动指定根模型|Manual root|Racine manuelle|Корень вручную|Raíz manual
folder|输出文件夹|Output folder|Dossier de sortie|Папка вывода|Carpeta de salida
name|输出文件名（留空 = 武器代号）|Output name (blank = weapon code)|Nom de sortie (vide = code de l'arme)|Имя файла (пустое = код оружия)|Nombre de salida (vacío = código del arma)
browse|浏览…|Browse…|Parcourir…|Обзор…|Examinar…
run|开始合并|Merge|Fusionner|Объединить|Fusionar
runAll|合并所有已就绪组|Merge all ready groups|Fusionner tous les groupes prêts|Объединить готовые группы|Fusionar grupos preparados
cancel|取消|Cancel|Annuler|Отмена|Cancelar
idle|待添加部件|Add parts to begin|Ajouter des pièces pour commencer|Добавьте части|Añada piezas para empezar
ready|已就绪|Ready|Prêt|Готово|Preparado
queued|排队中|Queued|En attente|В очереди|En cola
running|处理中|Running|En cours|Выполняется|Procesando
completed|已完成|Completed|Terminé|Завершено|Completado
cancelled|已取消|Cancelled|Annulé|Отменено|Cancelado
failed|失败|Failed|Échec|Ошибка|Error
validating|验证输入|Validating|Validation|Проверка|Validando
loading|读取模型|Loading|Chargement|Загрузка|Cargando
selecting_root|识别根模型|Selecting root|Sélection de la racine|Выбор корня|Seleccionando raíz
merging|合并网格|Merging|Fusion|Объединение|Fusionando
saving|写入临时文件|Saving temporary file|Écriture temporaire|Запись временного файла|Guardando archivo temporal
verifying|重新读取验证|Verifying output|Vérification du résultat|Проверка результата|Verificando salida
logs|运行日志|Task log|Journal|Журнал|Registro
save|保存设置|Save settings|Enregistrer les réglages|Сохранить настройки|Guardar ajustes
saved|设置已保存；不保存所选模型路径。|Settings saved; selected model paths are not stored.|Réglages enregistrés ; chemins des modèles non conservés.|Настройки сохранены; пути моделей не сохраняются.|Ajustes guardados; no se guardan las rutas de modelos.
reset|恢复默认设置|Reset settings|Réinitialiser|Сбросить настройки|Restablecer ajustes
capacity|每组最多 15 个不同的 CAST 文件；重复或超额文件未加入。|Each group accepts 15 distinct CAST files; duplicates or excess files were skipped.|15 fichiers CAST distincts maximum ; doublons et excédents ignorés.|Максимум 15 разных CAST; дубликаты и лишние пропущены.|Máximo 15 CAST distintos; se omitieron duplicados y excedentes.
invalid|请选择 2–15 个存在的 CAST 文件、有效输出目录，并选择手动根模型。|Choose 2–15 existing CAST files, an output folder, and a manual root when required.|Choisissez 2–15 CAST existants, un dossier et une racine manuelle si nécessaire.|Выберите 2–15 CAST, папку вывода и ручной корень при необходимости.|Elija 2–15 CAST existentes, carpeta y raíz manual si corresponde.
overwrite|输出文件已存在，是否覆盖？|Output exists. Replace it?|Le fichier existe. Le remplacer ?|Файл существует. Заменить?|El archivo existe. ¿Reemplazarlo?
yes|确认覆盖|Replace file|Remplacer le fichier|Заменить файл|Reemplazar archivo
close|关闭|Close|Fermer|Закрыть|Cerrar
ammo|弹匣装填|Ammunition filling|Remplissage des chargeurs|Заполнение магазинов|Rellenar cargadores
weapon|武器 / 弹匣 CAST|Weapon / magazine CAST|Arme / chargeur CAST|Оружие / магазин CAST|Arma / cargador CAST
ammunition|子弹 CAST（单个 tag_ammo 骨骼）|Ammunition CAST (single tag_ammo bone)|Munition CAST (un seul os tag_ammo)|Патрон CAST (одна кость tag_ammo)|Munición CAST (un hueso tag_ammo)
output|另存新文件|Save as a new file|Enregistrer un nouveau fichier|Сохранить в новый файл|Guardar como archivo nuevo
inspect|识别骨骼|Analyze bones|Analyser les os|Анализ костей|Analizar huesos
magazines|弹匣（默认仅装填第一组）|Magazines (first selected by default)|Chargeurs (premier sélectionné)|Магазины (по умолчанию первый)|Cargadores (primero por defecto)
source|复制来源|Layout source|Source de disposition|Источник раскладки|Origen de distribución
extras|其他子弹骨骼（按需勾选）|Other ammunition bones (opt-in)|Autres os de munitions (facultatif)|Другие кости патронов (по выбору)|Otros huesos de munición (opcional)
spares|备用弹匣（复制来源布局，默认不选）|Spare magazines (copy source layout; opt-in)|Chargeurs de réserve (copie facultative)|Запасные магазины (копия по выбору)|Cargadores de reserva (copia opcional)
ammoHint|只装填空槽位；已有网格自动跳过。最多 512 槽位 / 500 万新增顶点。必须另存新文件。|Only empty slots are filled. Up to 512 slots / 5 million added vertices. A new output file is required.|Seuls les emplacements libres sont remplis. 512 emplacements / 5 millions de sommets maximum. Nouveau fichier requis.|Заполняются только пустые места. До 512 мест / 5 млн вершин. Нужен новый файл.|Solo se rellenan huecos vacíos. Hasta 512 huecos / 5 millones de vértices. Se requiere archivo nuevo.
ammoInvalid|先识别武器骨骼，选择子弹模型、目标槽位及尚不存在的输出文件。|Analyze the weapon, select ammunition and targets, and choose a new output file.|Analysez l’arme, choisissez munition et cibles, puis un nouveau fichier.|Проанализируйте оружие, выберите патрон, цели и новый файл.|Analice el arma, elija munición y destinos, y un archivo nuevo.
empty|未识别到可用目标|No recognized targets|Aucune cible reconnue|Цели не найдены|No hay destinos reconocidos
slots|槽位 / 已占用|Slots / occupied|Emplacements / occupés|Места / занято|Huecos / ocupados
inserted|已装填 / 跳过|Inserted / skipped|Insérés / ignorés|Добавлено / пропущено|Insertados / omitidos
about|关于与许可|About and licenses|À propos et licences|О программе и лицензиях|Acerca de y licencias
aboutText|集成 ModelMergerGUI 2.5.0 的原生合并、填弹与手臂拼接引擎。MIT：Philip / Scobalula、echo000、ez4cywa。主题、软件更新与主窗口设置沿用 Alchemy Stars。|Native merge, ammunition and arm-assembly engine from ModelMergerGUI 2.5.0. MIT: Philip / Scobalula, echo000, ez4cywa. Theme, application updates and main-window settings are managed by Alchemy Stars.|Moteur natif ModelMergerGUI 2.5.0 : fusion, munitions et assemblage des bras. MIT : Philip / Scobalula, echo000, ez4cywa. Thème, mises à jour et fenêtre gérés par Alchemy Stars.|Нативный движок ModelMergerGUI 2.5.0: слияние, магазины и сборка рук. MIT: Philip / Scobalula, echo000, ez4cywa. Тема, обновления и окно управляются Alchemy Stars.|Motor nativo ModelMergerGUI 2.5.0: fusión, munición y ensamblado de brazos. MIT: Philip / Scobalula, echo000, ez4cywa. Tema, actualizaciones y ventana gestionados por Alchemy Stars.
repository|项目主页|Repository|Dépôt|Репозиторий|Repositorio
issues|反馈问题|Report an issue|Signaler un problème|Сообщить о проблеме|Informar problema
releases|下载与更新|Downloads and updates|Téléchargements|Загрузки и обновления|Descargas y actualizaciones
copy|复制项目链接|Copy repository link|Copier le lien|Копировать ссылку|Copiar enlace
copied|已复制|Copied|Copié|Скопировано|Copiado
deleteGroup|删除此组|Remove group|Supprimer le groupe|Удалить группу|Eliminar grupo
language|模块语言|Module language|Langue du module|Язык модуля|Idioma del módulo
defaults|新组使用最后保存的输出目录与根模式。主题与更新请使用软件设置。|New groups use saved output and root mode. Theme and updates are in application settings.|Nouveaux groupes : dossier et mode enregistrés. Thème et mises à jour dans les réglages de l’application.|Новые группы используют сохранённые папку и режим. Тема и обновления — в настройках приложения.|Los nuevos grupos usan carpeta y modo guardados. Tema y actualizaciones en ajustes de la aplicación.
fileMissing|文件不存在|File not found|Fichier introuvable|Файл не найден|Archivo no encontrado
cancelAll|取消全部任务|Cancel all tasks|Annuler toutes les tâches|Отменить все задачи|Cancelar todas las tareas
rootSelected|根模型|Root model|Modèle racine|Корневая модель|Modelo raíz
settingsError|设置无法读取或写入|Cannot read or save settings|Impossible de lire ou enregistrer les réglages|Ошибка чтения или записи настроек|No se pueden leer o guardar ajustes
cancelBeforeClose|仍有任务运行，请先取消或等待完成。|Tasks are active. Cancel them or wait before closing.|Des tâches sont actives. Annulez-les ou attendez.|Есть активные задачи. Отмените их или дождитесь завершения.|Hay tareas activas. Cancélelas o espere antes de cerrar.
armAssembly|手臂武器拼接|Attach weapon to arms|Fixer l'arme aux bras|Прикрепить оружие к рукам|Acoplar arma a los brazos
armAssemblyHint|启用后，每个模型组出现"手臂武器拼接"按钮。|When enabled, each group gains an "Attach weapon to arms" button.|Une fois activé, chaque groupe gagne un bouton « Fixer l'arme aux bras ».|После включения у каждой группы появляется кнопка «Прикрепить оружие к рукам».|Al activarlo, cada grupo obtiene un botón «Acoplar arma a los brazos».
arms|手臂模型（viewhands）|Arms model (viewhands)|Modèle de bras (viewhands)|Модель рук (viewhands)|Modelo de brazos (viewhands)
weaponModel|武器模型|Weapon model|Modèle d'arme|Модель оружия|Modelo de arma
targetBone|目标骨骼（自动探测 tag_weapon，可改选）|Target bone (auto-detects tag_weapon, overridable)|Os cible (tag_weapon détecté, modifiable)|Целевая кость (авто tag_weapon, можно изменить)|Hueso de destino (tag_weapon automático, modificable)
autoBone|自动（tag_weapon）|Auto (tag_weapon)|Auto (tag_weapon)|Авто (tag_weapon)|Automático (tag_weapon)
assemblyHint|把手臂与武器拼接成完整的第一人称模型：武器根骨骼零位移挂接到手臂的 tag_weapon，另存新 CAST。|Assemble the arms and weapon into a first-person model: the weapon root bone is zeroed onto the arms' tag_weapon and saved as a new CAST.|Assemblez les bras et l'arme en un modèle à la première personne : l'os racine de l'arme est aligné sur le tag_weapon des bras et enregistré dans un nouveau CAST.|Соберите руки и оружие в модель от первого лица: корневая кость оружия обнуляется на tag_weapon рук и сохраняется в новый CAST.|Ensambla brazos y arma en un modelo en primera persona: el hueso raíz del arma se alinea con el tag_weapon de los brazos y se guarda en un CAST nuevo.
attachedMeshes|已拼接网格|Attached meshes|Maillages assemblés|Присоединено сеток|Mallas acopladas
armAssemblyInvalid|先识别手臂骨骼，选择武器模型及尚不存在的输出文件。|Inspect the arms, choose a weapon model, and pick a new output file.|Analysez les bras, choisissez une arme et un nouveau fichier.|Проанализируйте руки, выберите оружие и новый файл.|Analice los brazos, elija un arma y un archivo nuevo.
""";
}
