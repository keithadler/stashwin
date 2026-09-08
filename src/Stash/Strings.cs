using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

namespace Stash;

/// <summary>English in the code, Spanish from a table, chosen by Windows' display language, like the Mac app's
/// Localizable.strings. T() for a literal, F() for a format string, Localize() walks a window built in XAML.</summary>
/// <summary>Strings data templates reach through x:Static, since templates are stamped out after Localize() has walked the tree.</summary>
public static class S
{
    public static string Remove => L.T("Remove");
    public static string RestoreDots => L.T("Restore…");
    public static string Delete => L.T("Delete");
    public static string Use => L.T("Use ");
}

public static class L
{
    public static bool Spanish { get; set; } = (Environment.GetEnvironmentVariable("STASH_LANG") ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).StartsWith("es", StringComparison.OrdinalIgnoreCase);

    public static string T(string en) => Spanish && Es.TryGetValue(en, out var s) ? s : en;
    public static string F(string en, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(en), args);

    /// <summary>Translates every literal string in a window's tree: TextBlock text and runs, button and check box content, the title.</summary>
    public static void Localize(DependencyObject root)
    {
        if (!Spanish) return;
        if (root is Window w && w.Title.Length > 0) w.Title = T(w.Title);
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject d) continue;
            switch (d)
            {
                case TextBlock tb when tb.Inlines.Count > 0:
                    if (BindingOperations.GetBinding(tb, TextBlock.TextProperty) is null && tb.Inlines.Count == 1 && tb.Inlines.FirstInline is Run only && only.Text.Length > 0 && BindingOperations.GetBinding(only, Run.TextProperty) is null)
                        tb.Text = T(only.Text); // a plain Text= block: set it whole, which replaces the implicit run
                    else
                        foreach (var inline in tb.Inlines.ToList())
                        {
                            if (inline is Run r && r.Text.Length > 0 && BindingOperations.GetBinding(r, Run.TextProperty) is null) r.Text = T(r.Text);
                            if (inline is Hyperlink h) foreach (var hi in h.Inlines.ToList()) if (hi is Run hr) hr.Text = T(hr.Text);
                        }
                    break;
                case ContentControl cc when cc.Content is string s && BindingOperations.GetBinding(cc, ContentControl.ContentProperty) is null:
                    cc.Content = T(s);
                    break;
            }
            Localize(d);
        }
    }

    public static readonly Dictionary<string, string> Es = new()
    {
        ["Stash for Windows"] = "Stash for Windows",
        ["Encrypted backup of the folders that matter, into storage you already have."] = "Copia de seguridad cifrada de las carpetas que importan, en el almacenamiento que ya tienes.",
        ["Settings"] = "Ajustes", ["Help"] = "Ayuda", ["About"] = "Acerca de",
        ["Step 1 of 3: a key"] = "Paso 1 de 3: una clave", ["Your key"] = "Tu clave",
        ["Stash makes it for you; you keep it as 24 words on a card. Nothing else can read the backup, and the app never keeps a copy of the card. The same card works in Stash for Mac."] = "Stash la crea por ti; tú la guardas como 24 palabras en una tarjeta. Nada más puede leer la copia, y la app nunca guarda una copia de la tarjeta. La misma tarjeta sirve en Stash for Mac.",
        ["Make a key"] = "Crear clave", ["I have a card"] = "Tengo una tarjeta", ["Show the recovery card"] = "Ver la tarjeta de recuperación", ["Forget this key…"] = "Olvidar esta clave…",
        ["Folders to protect"] = "Carpetas a proteger",
        ["Nothing yet. Documents, Pictures, Desktop, a project folder: whatever you would miss."] = "Nada aún. Documentos, Imágenes, Escritorio, una carpeta de proyecto: lo que echarías de menos.",
        ["Add folder…"] = "Añadir carpeta…", ["Where it goes"] = "Dónde va",
        ["Two destinations are safer: an account can be lost, a disk can fail."] = "Dos destinos son más seguros: una cuenta se puede perder, un disco puede fallar.",
        ["Found on this PC:"] = "Encontrado en este PC:", ["Add destination…"] = "Añadir destino…",
        ["Any folder works: the OneDrive, Google Drive, Dropbox or iCloud folder their apps sync, an external disk, a NAS. Everything written there is encrypted first."] = "Vale cualquier carpeta: la de OneDrive, Google Drive, Dropbox o iCloud que sincronizan sus apps, un disco externo, un NAS. Todo lo que se escribe ahí se cifra antes.",
        ["Snapshots"] = "Instantáneas", ["No backup yet."] = "Aún no hay copia.",
        ["A snapshot is small on its own: unchanged files are stored once and shared. \"Frees\" is what deleting that snapshot alone would give back."] = "Una instantánea es pequeña por sí sola: los archivos sin cambios se guardan una vez y se comparten. \"Libera\" es lo que devolvería borrar solo esa instantánea.",
        ["Keyboard: Ctrl+K the key or card, Ctrl+O add a folder, Ctrl+D add a destination, Ctrl+Enter back up, Ctrl+T verify, Ctrl+, settings."] = "Teclado: Ctrl+K la clave o la tarjeta, Ctrl+O añadir carpeta, Ctrl+D añadir destino, Ctrl+Intro copiar, Ctrl+T verificar, Ctrl+, ajustes.",
        ["Nothing leaves the PC unencrypted. You can keep working."] = "Nada sale del PC sin cifrar. Puedes seguir trabajando.",
        ["Verify"] = "Verificar", ["Back Up Now"] = "Copiar ahora", ["Remove"] = "Quitar", ["Restore…"] = "Restaurar…", ["Delete"] = "Borrar", ["Use "] = "Usar ",
        ["See the release"] = "Ver la versión", ["Skip this version"] = "Omitir esta versión",
        // Shell lines
        ["No key on this PC yet."] = "Aún no hay clave en este PC.",
        ["Key {0} on this PC, card confirmed."] = "Clave {0} en este PC, tarjeta confirmada.", ["Key {0} on this PC, card not yet confirmed."] = "Clave {0} en este PC, tarjeta aún sin confirmar.",
        ["Step 1: make a key, or enter the card from another PC or Mac."] = "Paso 1: crea una clave, o introduce la tarjeta de otro PC o Mac.",
        ["Step 2: add a folder to protect."] = "Paso 2: añade una carpeta a proteger.", ["Step 3: choose where it goes."] = "Paso 3: elige dónde va.",
        ["Last backup {0}. {1}"] = "Última copia {0}. {1}", ["Ready. Press Back Up Now."] = "Listo. Pulsa Copiar ahora.",
        ["Runs every hour."] = "Se ejecuta cada hora.", ["Runs once a day."] = "Se ejecuta una vez al día.", ["Runs only when you press Back Up Now."] = "Solo se ejecuta cuando pulsas Copiar ahora.",
        ["Last checked {0}."] = "Última comprobación {0}.", ["Never checked yet: Verify restores one random file to prove the whole path works."] = "Nunca comprobada: Verificar restaura un archivo al azar para demostrar que todo el camino funciona.",
        ["Not reachable right now; skipped until it is back."] = "No disponible ahora; se omite hasta que vuelva.", ["No backup here yet."] = "Aún no hay copia aquí.",
        ["{0} snapshots, {1} used, last {2}."] = "{0} instantáneas, {1} usados, última {2}.", ["{0} snapshot, {1} used, last {2}."] = "{0} instantánea, {1} usados, última {2}.",
        ["{0} files, {1}, from {2}"] = "{0} archivos, {1}, de {2}", [", {0} cloud placeholders listed"] = ", {0} marcadores en la nube listados", [". Frees {0}."] = ". Libera {0}.",
        ["Not found right now."] = "No se encuentra ahora.", ["Folder"] = "Carpeta", ["Network"] = "Red",
        ["Version {0} is available."] = "La versión {0} está disponible.",
        // Dialogs
        ["Your recovery card"] = "Tu tarjeta de recuperación",
        ["Write these 24 words down, or save the card, then put it somewhere safe. Stash does not keep a copy. The same card works in Stash for Mac."] = "Anota estas 24 palabras, o guarda la tarjeta, y ponla en un lugar seguro. Stash no guarda ninguna copia. La misma tarjeta sirve en Stash for Mac.",
        ["Key fingerprint {0}. Two cards can be told apart by it."] = "Huella de la clave {0}. Distingue dos tarjetas.", ["Scan with a phone or a Mac."] = "Escanéalo con un teléfono o un Mac.",
        ["Save as image…"] = "Guardar como imagen…", ["Save as text…"] = "Guardar como texto…", ["Print…"] = "Imprimir…", ["Copy the words"] = "Copiar las palabras",
        ["Prove the card is safe: type these three words from it."] = "Demuestra que la tarjeta está a salvo: escribe estas tres palabras.", ["Word {0}"] = "Palabra {0}",
        ["That does not match the card. Check the numbers and try again."] = "No coincide con la tarjeta. Revisa los números e inténtalo otra vez.",
        ["Close"] = "Cerrar", ["Later"] = "Más tarde", ["I have the card"] = "Tengo la tarjeta",
        ["Enter a recovery card"] = "Introducir una tarjeta de recuperación",
        ["The 24 words in order, any spacing or capitals, or the text behind the card's QR code. A wrong word is caught before anything happens."] = "Las 24 palabras en orden, con cualquier espaciado o mayúsculas, o el texto del código QR de la tarjeta. Una palabra errónea se detecta antes de que pase nada.",
        ["Cancel"] = "Cancelar", ["Use this key"] = "Usar esta clave",
        ["Restore"] = "Restaurar", ["Restore from {0}"] = "Restaurar de {0}",
        ["Select files (Ctrl-click for several), or restore everything. Restored files go into a folder named after the original, in the place you choose. Nothing at the original location is touched."] = "Selecciona archivos (Ctrl+clic para varios), o restaura todo. Los archivos restaurados van a una carpeta con el nombre de la original, donde tú elijas. Nada en la ubicación original se toca.",
        ["Where: choose a folder"] = "Dónde: elige una carpeta", ["Where: "] = "Dónde: ", ["Choose folder…"] = "Elegir carpeta…", ["Restore into"] = "Restaurar en",
        ["Restore selected"] = "Restaurar seleccionados", ["Restore everything"] = "Restaurar todo",
        ["Choose where the files should go first."] = "Elige primero dónde deben ir los archivos.", ["Select some files, or restore everything."] = "Selecciona algunos archivos, o restaura todo.",
        ["When to back up"] = "Cuándo copiar", ["Only when I press Back Up Now"] = "Solo cuando pulso Copiar ahora", ["Every hour"] = "Cada hora", ["Once a day, at 20:00"] = "Una vez al día, a las 20:00",
        ["A Task Scheduler task for this user runs the backup whether or not the window is open, only to destinations that are reachable, uploading only what changed."] = "Una tarea del Programador de tareas para este usuario hace la copia esté o no abierta la ventana, solo a los destinos disponibles, subiendo solo lo que cambió.",
        ["How many snapshots to keep"] = "Cuántas instantáneas conservar", ["The newest"] = "Las más recientes", [" snapshots"] = " instantáneas",
        ["Thin out over time: every one from the last week, one a day for a month, one a week for a year, one a month after that"] = "Aclarar con el tiempo: todas las de la última semana, una al día durante un mes, una a la semana durante un año, una al mes después",
        ["Older snapshots and the pieces only they used are deleted after each backup, so the destination stays a sensible size."] = "Las instantáneas antiguas y las piezas que solo ellas usaban se borran tras cada copia, para que el destino tenga un tamaño razonable.",
        ["Skip"] = "Omitir", ["Skip files larger than "] = "Omitir archivos de más de ", [" MB (empty: no limit)"] = " MB (vacío: sin límite)",
        ["Name patterns, comma separated. Files and folders whose name matches are left out. Virtual machine disks are skipped by default: they are huge and change constantly."] = "Patrones de nombre, separados por comas. Los archivos y carpetas cuyo nombre coincide se dejan fuera. Los discos de máquinas virtuales se omiten por defecto: son enormes y cambian constantemente.",
        ["Check the backup once a week: open every piece and restore one random file"] = "Comprobar la copia una vez a la semana: abrir cada pieza y restaurar un archivo al azar",
        ["The app"] = "La app", ["Stay in the tray when the window closes, with the last and next backup"] = "Quedarse en la bandeja al cerrar la ventana, con la última copia y la siguiente",
        ["Open at sign-in, in the tray"] = "Abrir al iniciar sesión, en la bandeja", ["Check GitHub once a day for a new version"] = "Consultar GitHub una vez al día por una versión nueva",
        ["The update check is one request for a version number, with no identifiers, and the only thing this app ever sends anywhere other than your destinations. A new version is offered as a link; nothing installs by itself."] = "La comprobación de versión es una sola petición de un número de versión, sin identificadores, y lo único que esta app envía fuera de tus destinos. Una versión nueva se ofrece como enlace; nada se instala solo.",
        ["Save"] = "Guardar",
        ["About Stash for Windows"] = "Acerca de Stash for Windows", ["Version {0}, MIT licence"] = "Versión {0}, licencia MIT",
        ["Source and releases"] = "Código y versiones", ["More from the same maker"] = "Más del mismo autor",
        // Messages
        ["Remove the key from this PC? The backups stay where they are, encrypted, and only the card can read them again. Make sure you have the card."] = "¿Quitar la clave de este PC? Las copias siguen donde están, cifradas, y solo la tarjeta puede volver a leerlas. Asegúrate de tener la tarjeta.",
        ["Folder to protect"] = "Carpeta a proteger", ["Where the encrypted backup goes"] = "Dónde va la copia cifrada",
        ["You have not confirmed the recovery card yet. Without it the backup can never be read. Show the card now?"] = "Aún no has confirmado la tarjeta de recuperación. Sin ella la copia no se podrá leer nunca. ¿Ver la tarjeta ahora?",
        ["Backing up"] = "Copiando", ["Checking the backup"] = "Comprobando la copia",
        ["Delete the snapshot from {0}? Files that exist only in this snapshot are gone for good; files that also exist in other snapshots are unaffected. Frees {1}."] = "¿Borrar la instantánea de {0}? Los archivos que solo existen en esta instantánea desaparecen para siempre; los que también están en otras no se ven afectados. Libera {1}.",
        ["Could not open Help: "] = "No se pudo abrir la ayuda: ",
        ["Open Stash"] = "Abrir Stash", ["Quit"] = "Salir", ["No backup yet"] = "Aún sin copia", ["Last backup {0}"] = "Última copia {0}", ["Next about {0}"] = "Siguiente hacia {0}", ["No schedule"] = "Sin programación", ["Next: on the schedule"] = "Siguiente: según la programación",
        ["The scheduled backup skipped something. Open Stash to see what."] = "La copia programada omitió algo. Abre Stash para ver qué.", ["The scheduled backup or check failed. Open Stash to see why."] = "La copia o comprobación programada falló. Abre Stash para ver por qué.",
    };
}
