// EuroWatcher - the few texts the Windows program and its installer show themselves.
// The page has its own, larger set of translations in i18n.js.

using System.Collections.Generic;
using System.Globalization;

namespace EuroWatcher
{
    internal static class Strings
    {
        static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>();
        static readonly string[] Languages = { "en", "fr", "es", "pt", "de", "nl", "it" };
        static int _lang = -1;

        static int Lang
        {
            get
            {
                if (_lang < 0)
                {
                    string code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                    _lang = System.Array.IndexOf(Languages, code);
                    if (_lang < 0) _lang = 0;
                }
                return _lang;
            }
        }

        /// <summary>The text for the Windows display language (English when it is not one of ours).</summary>
        public static string Get(string key)
        {
            string[] row;
            if (!Table.TryGetValue(key, out row)) return key;
            return string.IsNullOrEmpty(row[Lang]) ? row[0] : row[Lang];
        }

        public static string Format(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, Get(key), args);
        }

        static void Add(string key, string en, string fr, string es, string pt, string de, string nl, string it)
        {
            Table[key] = new string[] { en, fr, es, pt, de, nl, it };
        }

        static Strings()
        {
            Add("ShortcutDescription",
                "Every EuroMillions draw, checked against your numbers",
                "Tous les tirages EuroMillions, comparés à vos numéros",
                "Todos los sorteos de EuroMillones, comparados con tus números",
                "Todos os sorteios do Euromilhões, comparados com os seus números",
                "Alle EuroMillions-Ziehungen, mit Ihren Zahlen verglichen",
                "Alle EuroMillions-trekkingen, vergeleken met je nummers",
                "Tutte le estrazioni EuroMillions, confrontate con i tuoi numeri");

            Add("SetupTitle", "Install EuroWatcher", "Installer EuroWatcher", "Instalar EuroWatcher", "Instalar o EuroWatcher",
                "EuroWatcher installieren", "EuroWatcher installeren", "Installa EuroWatcher");
            Add("SetupBody",
                "EuroWatcher shows every EuroMillions draw since 2004 and checks your numbers against them. It is installed for your Windows account only, so no administrator permission is needed.",
                "EuroWatcher affiche tous les tirages EuroMillions depuis 2004 et y compare vos numéros. Il s’installe pour votre compte Windows uniquement : aucune autorisation d’administrateur n’est nécessaire.",
                "EuroWatcher muestra todos los sorteos de EuroMillones desde 2004 y los compara con tus números. Se instala solo para tu cuenta de Windows, así que no hace falta permiso de administrador.",
                "O EuroWatcher mostra todos os sorteios do Euromilhões desde 2004 e compara-os com os seus números. É instalado apenas para a sua conta do Windows, por isso não precisa de permissão de administrador.",
                "EuroWatcher zeigt alle EuroMillions-Ziehungen seit 2004 und vergleicht sie mit Ihren Zahlen. Es wird nur für Ihr Windows-Konto installiert, Administratorrechte sind nicht nötig.",
                "EuroWatcher toont alle EuroMillions-trekkingen sinds 2004 en vergelijkt ze met je nummers. Het wordt alleen voor je eigen Windows-account geïnstalleerd, dus beheerdersrechten zijn niet nodig.",
                "EuroWatcher mostra tutte le estrazioni EuroMillions dal 2004 e le confronta con i tuoi numeri. Viene installato solo per il tuo account Windows, quindi non servono i permessi di amministratore.");
            Add("DesktopShortcut", "Add a shortcut to the desktop", "Ajouter un raccourci sur le bureau", "Añadir un acceso directo al escritorio",
                "Adicionar um atalho ao ambiente de trabalho", "Verknüpfung auf dem Desktop anlegen", "Snelkoppeling op het bureaublad zetten", "Aggiungi un collegamento sul desktop");
            Add("OpenAfter", "Open EuroWatcher when done", "Ouvrir EuroWatcher à la fin", "Abrir EuroWatcher al terminar", "Abrir o EuroWatcher no fim",
                "EuroWatcher danach öffnen", "EuroWatcher daarna openen", "Apri EuroWatcher al termine");
            Add("Install", "Install", "Installer", "Instalar", "Instalar", "Installieren", "Installeren", "Installa");
            Add("Cancel", "Cancel", "Annuler", "Cancelar", "Cancelar", "Abbrechen", "Annuleren", "Annulla");
            Add("Close", "Close", "Fermer", "Cerrar", "Fechar", "Schließen", "Sluiten", "Chiudi");
            Add("Working", "Please wait…", "Veuillez patienter…", "Espera un momento…", "Aguarde…", "Bitte warten…", "Even geduld…", "Attendere…");
            Add("Installed", "EuroWatcher is installed. You will find it in the Start menu.",
                "EuroWatcher est installé. Vous le trouverez dans le menu Démarrer.",
                "EuroWatcher está instalado. Lo encontrarás en el menú Inicio.",
                "O EuroWatcher está instalado. Encontra-o no menu Iniciar.",
                "EuroWatcher ist installiert. Sie finden es im Startmenü.",
                "EuroWatcher is geïnstalleerd. Je vindt het in het Startmenu.",
                "EuroWatcher è installato. Lo trovi nel menu Start.");
            Add("InstallFailed", "The installation did not work: {0}", "L’installation a échoué : {0}", "La instalación ha fallado: {0}",
                "A instalação falhou: {0}", "Die Installation hat nicht geklappt: {0}", "De installatie is mislukt: {0}", "L’installazione non è riuscita: {0}");

            Add("UninstallTitle", "Remove EuroWatcher", "Désinstaller EuroWatcher", "Desinstalar EuroWatcher", "Remover o EuroWatcher",
                "EuroWatcher entfernen", "EuroWatcher verwijderen", "Rimuovi EuroWatcher");
            Add("UninstallBody", "This removes EuroWatcher and its shortcuts from this PC.",
                "EuroWatcher et ses raccourcis seront supprimés de ce PC.",
                "Se quitarán EuroWatcher y sus accesos directos de este PC.",
                "O EuroWatcher e os seus atalhos serão removidos deste PC.",
                "EuroWatcher und seine Verknüpfungen werden von diesem PC entfernt.",
                "EuroWatcher en de snelkoppelingen worden van deze pc verwijderd.",
                "EuroWatcher e i suoi collegamenti verranno rimossi da questo PC.");
            Add("DeleteData", "Also delete my saved plays and the downloaded draw data",
                "Supprimer aussi mes grilles enregistrées et les tirages téléchargés",
                "Borrar también mis apuestas guardadas y los sorteos descargados",
                "Apagar também as minhas apostas guardadas e os sorteios transferidos",
                "Auch meine gespeicherten Tipps und die geladenen Ziehungen löschen",
                "Ook mijn bewaarde combinaties en de gedownloade trekkingen wissen",
                "Elimina anche le mie giocate salvate e le estrazioni scaricate");
            Add("Remove", "Remove", "Désinstaller", "Desinstalar", "Remover", "Entfernen", "Verwijderen", "Rimuovi");
            Add("Removed", "EuroWatcher has been removed.", "EuroWatcher a été désinstallé.", "EuroWatcher se ha desinstalado.",
                "O EuroWatcher foi removido.", "EuroWatcher wurde entfernt.", "EuroWatcher is verwijderd.", "EuroWatcher è stato rimosso.");
            Add("RemoveProblems", "EuroWatcher was removed, but some items could not be deleted:",
                "EuroWatcher a été désinstallé, mais certains éléments n’ont pas pu être supprimés :",
                "EuroWatcher se ha desinstalado, pero algunos elementos no se han podido borrar:",
                "O EuroWatcher foi removido, mas alguns itens não puderam ser apagados:",
                "EuroWatcher wurde entfernt, aber einige Elemente konnten nicht gelöscht werden:",
                "EuroWatcher is verwijderd, maar sommige onderdelen konden niet gewist worden:",
                "EuroWatcher è stato rimosso, ma alcuni elementi non sono stati eliminati:");

            Add("NoBrowser",
                "EuroWatcher opened in your default browser because neither Microsoft Edge nor Google Chrome was found. Close it from this icon when you are done.",
                "EuroWatcher s’est ouvert dans votre navigateur par défaut, car ni Microsoft Edge ni Google Chrome n’ont été trouvés. Fermez-le depuis cette icône quand vous avez fini.",
                "EuroWatcher se ha abierto en tu navegador predeterminado porque no se ha encontrado Microsoft Edge ni Google Chrome. Ciérralo desde este icono cuando termines.",
                "O EuroWatcher abriu no seu navegador predefinido porque não foi encontrado o Microsoft Edge nem o Google Chrome. Feche-o a partir deste ícone quando terminar.",
                "EuroWatcher wurde in Ihrem Standardbrowser geöffnet, weil weder Microsoft Edge noch Google Chrome gefunden wurde. Schließen Sie es über dieses Symbol, wenn Sie fertig sind.",
                "EuroWatcher is geopend in je standaardbrowser, omdat Microsoft Edge en Google Chrome niet gevonden werden. Sluit het via dit pictogram als je klaar bent.",
                "EuroWatcher si è aperto nel browser predefinito perché non sono stati trovati né Microsoft Edge né Google Chrome. Chiudilo da questa icona quando hai finito.");
            Add("TrayOpen", "Open EuroWatcher", "Ouvrir EuroWatcher", "Abrir EuroWatcher", "Abrir o EuroWatcher", "EuroWatcher öffnen", "EuroWatcher openen", "Apri EuroWatcher");
            Add("TrayExit", "Close EuroWatcher", "Fermer EuroWatcher", "Cerrar EuroWatcher", "Fechar o EuroWatcher", "EuroWatcher beenden", "EuroWatcher sluiten", "Chiudi EuroWatcher");
            Add("StartFailed", "EuroWatcher could not start: {0}", "EuroWatcher n’a pas pu démarrer : {0}", "EuroWatcher no ha podido iniciarse: {0}",
                "O EuroWatcher não conseguiu arrancar: {0}", "EuroWatcher konnte nicht starten: {0}", "EuroWatcher kon niet starten: {0}", "Impossibile avviare EuroWatcher: {0}");
        }
    }
}
