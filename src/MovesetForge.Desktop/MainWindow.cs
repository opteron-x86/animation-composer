using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MovesetForge.Core;

namespace MovesetForge.Desktop;

public sealed record PreviewFolder(Moveset Set)
{
    public override string ToString()=>$"{Set.Config["name"]?.GetValue<string>()??Path.GetFileName(Set.Folder)} · {Path.GetFileName(Set.Folder)} · {Set.Animations.Count} clips";
}
public sealed partial class MainWindow:Window
{
    private Project project=new();private Library library=new();private List<Bundle> bundles=[];
    private readonly TextBox source=new(){Watermark="Mod-manager instance, mods folder, or extracted moveset",MinWidth=220};
    private readonly ComboBox profiles=new(){MinWidth=135,ItemsSource=new[]{"All installed"},SelectedIndex=0};
    private readonly TextBox search=new(){Watermark="Search movesets or source paths…"};
    private readonly CheckBox combatOnly=new(){Content="Combat movesets",IsChecked=true};
    private readonly ListBox sourceList=new();private readonly ListBox clips=new();
    private readonly DataGrid rules;
    private readonly ObservableCollection<AddedSetRow> addedSets = [];
    private readonly TextBlock stats=new(){Foreground=Brush.Parse("#90A3B5")};
    private readonly TextBlock status=new(){Text="Choose your animation library to begin.",TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock clipInfo=new(){Text="No animation selected",TextWrapping=TextWrapping.Wrap,Foreground=Brush.Parse("#9AABBB")};
    private readonly TextBox skeleton=new(){Watermark="Select skeleton.hkx",IsReadOnly=true};
    private readonly TextBox details=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};
    private readonly TextBox events=new(){IsReadOnly=true,AcceptsReturn=true};
    private readonly TextBox sequenceDetails=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};
    private readonly TextBox conversionDetails=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};
    private readonly ComboBox previewFolders=new(){HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly ComboBox sequenceOrder=new(){ItemsSource=new[]{"Follow annotations","All numbered clips"},SelectedIndex=0,MinWidth=150};
    private readonly Button normalChain=new(){Content="Play normal chain",IsEnabled=false},powerChain=new(){Content="Play power chain",IsEnabled=false};
    private AttackSequence? sequence;private double playbackTime;private int activeStep=-1;private bool selectingPlaybackClip;
    private readonly TextBox projectName=new(){Text="My Combat Movesets"};
    private readonly TextBox ruleName=new(){Text="Sword style"};
    private readonly ComboBox weapon=new(){ItemsSource=WeaponProfiles.All,SelectedIndex=0};
    private readonly TextBox keyword=new(){Watermark="Weapon keyword EditorID",IsVisible=false};
    private readonly TextBlock detectionInfo=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brush.Parse("#E7C180"),FontSize=11};
    private readonly TextBlock editorInfo=new(){Text="Select a source moveset",TextWrapping=TextWrapping.Wrap,FontWeight=FontWeight.SemiBold};
    private readonly ComboBox target=new(){ItemsSource=new[]{"BFCO + native OAR","OAR · keep original slots"},SelectedIndex=0};
    private readonly ComboBox actors=new(){ItemsSource=new[]{"Both","Player","NPCs","Source"},SelectedIndex=0};
    private readonly ComboBox hand=new(){ItemsSource=new[]{"Right","Left","Either","Both"},SelectedIndex=0};
    private readonly TextBox priority=new(){Text="20000"};
    private readonly TextBox levelMin=new(){Watermark="Min"},levelMax=new(){Watermark="Max"},skillMin=new(){Watermark="Min"},skillMax=new(){Watermark="Max"};
    private readonly ComboBox skill=new(){ItemsSource=new[]{"One-handed","Two-handed","Archery","Block"},SelectedIndex=0};
    private readonly TextBox racePlugin=new(){Watermark="Skyrim.esm"},raceId=new(){Watermark="Race FormID"},weaponPlugin=new(){Watermark="Weapon plugin"},weaponId=new(){Watermark="Weapon FormID"};
    private readonly TextBox advanced=new(){Text="[]",AcceptsReturn=true,MinHeight=90,FontFamily=new FontFamily("monospace")};
    private readonly PreviewViewport viewport=new();private readonly Slider timeline=new(){Minimum=0,Maximum=1};
    private readonly ComboBox speed=new(){ItemsSource=new[]{"0.25×","0.5×","1×","2×"},SelectedIndex=2,Width=80};
    private readonly Button play=new(){Content="Play"};private readonly CheckBox loop=new(){Content="Loop",IsChecked=true};
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(16)};private readonly Stopwatch clock=new();
    private double lastTick;private bool ticking,busy;private CancellationTokenSource? scanCancel;private int previewVersion;
    private SkeletonData? loadedSkeleton;private string loadedSkeletonPath="";private List<ProfileInfo> profileChoices=[];
    private readonly List<string> extraRoots=[];private readonly string[] args;
    private Rule? editorRule;private bool changingSelection,loadingEditor,restoringProject;

    public MainWindow(string[] args)
    {
        this.args=args;Title=App.DisplayName;Width=1480;Height=940;MinWidth=1040;MinHeight=740;FontFamily=new("Inter");FontSize=13;Background=Brush.Parse("#10151C");
        bool rememberLayout = !args.Contains("--snapshot");
        var savedLayout = rememberLayout ? LayoutPreferences.Load() : null;
        rules = CreateAddedSetsTable(savedLayout?.AddedSetColumns);
        weapon.TemplateApplied+=(_,e)=>
        {
            // Native popup hosts center their content at its desired size. Constrain
            // both the host and the visible list so virtualized items cannot shrink it.
            if(e.NameScope.Find<Popup>("PART_Popup") is {} popup)
            {
                popup.Bind(WidthProperty,new Binding("Bounds.Width"){Source=weapon});
                if(popup.Child is {} list)list.Bind(WidthProperty,new Binding("Bounds.Width"){Source=weapon});
                popup.Placement=PlacementMode.BottomEdgeAlignedLeft;
            }
        };
        var top=new Grid{ColumnDefinitions=new("*,Auto"),Margin=new Thickness(22,16)};
        top.Children.Add(new TextBlock{Text=App.DisplayName,FontSize=25,FontWeight=FontWeight.SemiBold,Foreground=Brush.Parse("#E7C180")});
        var files=Row(Button("New",NewProject),Button("Open project",OpenProject),Button("Save project",SaveProject),Button("Export mod",Export));Grid.SetColumn(files,1);top.Children.Add(files);
        var sourceRow=new Grid{ColumnDefinitions=new("*,Auto,150,Auto,Auto"),ColumnSpacing=8,Margin=new Thickness(22,0,22,14)};sourceRow.Children.Add(source);
        Put(sourceRow,Button("Browse…",BrowseSource),1);Put(sourceRow,profiles,2);Put(sourceRow,Button("Scan",Scan),3);Put(sourceRow,Button("Import ZIP / BSA",ImportArchive),4);
        var main=CreateWorkspace();
        rules.ItemsSource=addedSets;
        var footer=new StackPanel{Spacing=5,Margin=new Thickness(22,0,22,16)};footer.Children.Add(clipInfo);footer.Children.Add(status);
        var shell=new Grid{RowDefinitions=new("Auto,Auto,*,Auto")};shell.Children.Add(top);Put(shell,sourceRow,row:1);Put(shell,main,row:2);Put(shell,footer,row:3);
        Content = rememberLayout ? RememberLayout(shell, savedLayout) : shell;
        search.TextChanged+=(_,_)=>Filter();combatOnly.IsCheckedChanged+=(_,_)=>Filter();sourceList.SelectionChanged+=(_,_)=>SelectBundle();previewFolders.SelectionChanged+=(_,_)=>SelectPreviewFolder();clips.SelectionChanged+=async(_,_)=>{if(!selectingPlaybackClip)await LoadPreview();};rules.SelectionChanged+=(_,_)=>LoadRule();
        weapon.SelectionChanged+=(_,_)=>{if(weapon.SelectedItem is WeaponProfile p){keyword.IsVisible=p.Id.StartsWith("aa.",StringComparison.Ordinal)||p.Id=="keyword";if(!loadingEditor){keyword.Text=p.Keyword;skill.SelectedIndex=Array.IndexOf(new[]{6,7,8,9},p.Skill);}}};
        target.SelectionChanged+=(_,_)=>{if(loadingEditor)return;project.TargetFramework=target.SelectedIndex==1?"OAR":"BFCO";if(sourceList.SelectedItem is Bundle b)conversionDetails.Text=ConversionPlan.Describe(b.Sets,project.TargetFramework);if(target.SelectedIndex==1){combatOnly.IsChecked=false;if(editorRule!=null&&!project.Rules.Any(r=>r.Id==editorRule.Id)){weapon.SelectedItem=WeaponProfiles.Source;actors.SelectedItem="Source";}}};
        timeline.PropertyChanged+=(_,e)=>{if(e.Property==RangeBase.ValueProperty&&!ticking)SetPreviewTime(timeline.Value);};
        timer.Tick+=(_,_)=>Tick();Closing+=(_,_)=>{timer.Stop();scanCancel?.Cancel();};
        Opened+=async(_,_)=>{try{await Initialize();}catch(Exception e){status.Text=e.Message;}};
    }
    private static TextBlock Label(string text)=>new(){Text=text,FontWeight=FontWeight.SemiBold,FontSize=11,Foreground=Brush.Parse("#AFBDCB"),LetterSpacing=1};
    private static Border Card(Control content)=>new(){Background=Brush.Parse("#19222C"),BorderBrush=Brush.Parse("#2A3643"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Padding=new Thickness(12),Child=content};
    private static StackPanel Row(params Control[] controls){var p=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,VerticalAlignment=VerticalAlignment.Center};foreach(var c in controls)p.Children.Add(c);return p;}
    private static StackPanel Field(string name,Control c)=>new(){Spacing=4,Children={new TextBlock{Text=name,FontSize=11,Foreground=Brush.Parse("#A6B6C6")},c}};
    private static Grid Two(Control a,Control b){var g=new Grid{ColumnDefinitions=new("*,*"),ColumnSpacing=7};g.Children.Add(a);Put(g,b,1);return g;}
    private static void Put(Grid g,Control c,int col=0,int row=0){Grid.SetColumn(c,col);Grid.SetRow(c,row);g.Children.Add(c);}
    private Button Button(string text,Func<Task> action){var b=new Button{Content=text,Padding=new Thickness(10,7),CornerRadius=new CornerRadius(4)};b.Click+=async(_,_)=>{try{await action();}catch(Exception e){status.Text=e.Message;}};return b;}
    private async Task<string?> PickFolder(string title){var p=await StorageProvider.OpenFolderPickerAsync(new(){Title=title,AllowMultiple=false});return p.FirstOrDefault()?.TryGetLocalPath();}
    private async Task<string?> PickFile(string title,params string[] patterns){var p=await StorageProvider.OpenFilePickerAsync(new(){Title=title,AllowMultiple=false,FileTypeFilter=[new FilePickerFileType("Files"){Patterns=patterns}]});return p.FirstOrDefault()?.TryGetLocalPath();}
    private async Task BrowseSource(){if(await PickFolder("Choose mod-manager instance or animation folder") is {} p){source.Text=p;extraRoots.Clear();RefreshProfiles();}}
    private void RefreshProfiles()
    {
        string p=source.Text??"";string instance=Path.GetFileName(p) is "mods" or "profiles"?Path.GetDirectoryName(p)!:p;
        profileChoices=Profiles.List(instance);profiles.ItemsSource=new[]{"All installed"}.Concat(profileChoices.Select(p=>p.Name)).ToArray();profiles.SelectedIndex=0;
    }
    private async Task Scan()
    {
        if(busy){scanCancel?.Cancel();return;}busy=true;scanCancel=new();status.Text="Reading animation sources…";
        try
        {
            string p=source.Text??"";if(!Directory.Exists(p))throw new DirectoryNotFoundException("Choose an existing source folder.");
            string instance=Path.GetFileName(p) is "mods" or "profiles"?Path.GetDirectoryName(p)!:p;
            string mods=Directory.Exists(Path.Combine(instance,"mods"))?Path.Combine(instance,"mods"):p;
            var roots=restoringProject?project.SourceRoots.ToList():profiles.SelectedIndex>0?Profiles.EnabledFolders(mods,profileChoices[profiles.SelectedIndex-1]):new List<string>{mods};if(!restoringProject)roots.AddRange(extraRoots);
            if(!restoringProject)project.ApplyProfileOverrides=profiles.SelectedIndex>0;
            project.SourceRoots=roots;
            library=await Task.Run(()=>Scanner.Scan(roots,new Progress<string>(s=>Dispatcher.UIThread.Post(()=>{if(busy)status.Text=s;})),scanCancel.Token,project.ApplyProfileOverrides));
            bundles=Bundles.Create(library);
            Filter();stats.Text=$"{library.FileCount:N0} files · {bundles.Count:N0} sets";
            if(string.IsNullOrWhiteSpace(skeleton.Text)){var best=library.Skeletons.FirstOrDefault(s=>s.Replace('\\','/').Contains("actors/character/character assets/skeleton.hkx",StringComparison.OrdinalIgnoreCase));if(best!=null)skeleton.Text=best;}
            status.Text=$"Library ready. {library.Diagnostics.Count} scan notices. "+(project.ApplyProfileOverrides?"Enabled profile configuration overrides applied.":"Select an MO2 profile to apply config-only patches, including Animated Armoury configs.");if(library.Diagnostics.Count>0)details.Text=string.Join('\n',library.Diagnostics.Take(200));
        }
        catch(OperationCanceledException){status.Text="Scan canceled.";}finally{busy=false;}
    }
    private void Filter(){string q=search.Text??"";sourceList.ItemsSource=bundles.Where(b=>b.Sets.Any(s=>!s.MeshPrefix.Contains("_1stperson",StringComparison.OrdinalIgnoreCase))&&(combatOnly.IsChecked!=true||b.Combat)&&(b.Name.Contains(q,StringComparison.OrdinalIgnoreCase)||b.Sets.Any(s=>s.Folder.Contains(q,StringComparison.OrdinalIgnoreCase)))).ToList();}
    private void SelectBundle()
    {
        if(changingSelection||sourceList.SelectedItem is not Bundle b)return;
        try
        {
            CommitEditor();SelectAddedSet(null);editorRule=WeaponDetector.Draft(b.Name,b.Sets,project.Rules);
            if(project.TargetFramework=="OAR"){editorRule.WeaponProfile="source";editorRule.WeaponKeyword="";editorRule.Actors="Source";}
            LoadEditor(editorRule);detectionInfo.Text=WeaponDetector.Detect(b.Sets,b.Name).Summary;
        }
        catch(Exception e){status.Text="Finish the current rule first: "+e.Message;return;}
        ShowSources(b.Sets);
    }
    private void ShowSources(List<Moveset> sets)
    {
        details.Text=string.Join("\n\n",sets.Select(s=>s.Folder+"\nConfiguration: "+string.Join("; ",s.ConfigurationSources)+"\n"+s.Config.ToJsonString(Json.Options)));
        conversionDetails.Text=ConversionPlan.Describe(sets,project.TargetFramework);
        var choices=sets.Select(s=>new PreviewFolder(s)).ToArray();previewFolders.ItemsSource=choices;
        int Score(PreviewFolder folder){var slots=folder.Set.Animations.Select(a=>AttackSlot.FromPath(a.RelativePath)).Where(s=>s!=null).ToArray();return (slots.Any(s=>s!.Kind==AttackKind.Normal&&s.Number==1)?2:0)+(slots.Any(s=>s!.Kind==AttackKind.Power&&s.Number==1)?1:0);}
        previewFolders.SelectedItem=choices.OrderByDescending(Score).ThenBy(s=>s.Set.Config["priority"]?.GetValue<int>()??0).FirstOrDefault();
    }
    private void SelectPreviewFolder()
    {
        if(previewFolders.SelectedItem is not PreviewFolder folder){normalChain.IsEnabled=false;powerChain.IsEnabled=false;return;}
        var animations=folder.Set.Animations;clips.ItemsSource=animations;
        normalChain.IsEnabled=animations.Any(a=>AttackSlot.FromPath(a.RelativePath)?.Kind==AttackKind.Normal);
        powerChain.IsEnabled=animations.Any(a=>AttackSlot.FromPath(a.RelativePath)?.Kind==AttackKind.Power);
        clips.SelectedItem=animations.OrderByDescending(a=>AttackSlot.FromPath(a.RelativePath) is {Kind:AttackKind.Normal,Number:1}).ThenByDescending(a=>AttackSlot.FromPath(a.RelativePath)!=null).FirstOrDefault();
    }
    private async Task ChooseSkeleton(){if(await PickFile("Choose skeleton.hkx","*.hkx","*.HKX") is {} p){skeleton.Text=p;await LoadPreview();}}
    private async Task LoadPreview()
    {
        int request=BeginPreview();
        if(clips.SelectedItem is not AnimationFile a){viewport.EmptyTitle="Select an animation";viewport.EmptyDetail="Choose a source folder and a clip.";return;}
        string skel=skeleton.Text??"";if(!File.Exists(skel)){ShowPreviewError("Choose skeleton.hkx to preview this animation.");return;}
        clipInfo.Text="Loading "+a.Name+"…";
        try
        {
            var cached=loadedSkeletonPath==skel?loadedSkeleton:null;
            var result=await Task.Run(()=>{var bones=cached??Preview.Skeleton(skel);return (Bones:bones,Clip:Preview.Load(a.Path,bones));});
            if(request!=previewVersion)return;loadedSkeleton=result.Bones;loadedSkeletonPath=skel;SetSequence(AttackSequence.Single(a,result.Clip));
        }
        catch(Exception e){if(request==previewVersion)ShowPreviewError(e.Message);}
    }
    private async Task LoadChain(AttackKind kind)
    {
        if(previewFolders.SelectedItem is not PreviewFolder folder)return;
        var preferred=clips.SelectedItem as AnimationFile;var order=sequenceOrder.SelectedIndex==1?SequenceOrder.Numbered:SequenceOrder.Annotations;
        int request=BeginPreview();string skel=skeleton.Text??"";
        if(!File.Exists(skel)){ShowPreviewError("Choose skeleton.hkx to preview this chain.");return;}
        clipInfo.Text="Loading "+kind.ToString().ToLowerInvariant()+" attack chain…";
        try
        {
            var cached=loadedSkeletonPath==skel?loadedSkeleton:null;
            var result=await Task.Run(()=>{var bones=cached??Preview.Skeleton(skel);return (Bones:bones,Sequence:AttackSequence.Build(folder.Set.Animations,kind,order,file=>Preview.Load(file.Path,bones),preferred));});
            if(request!=previewVersion)return;loadedSkeleton=result.Bones;loadedSkeletonPath=skel;SetSequence(result.Sequence);TogglePlay();
            status.Text=result.Sequence.Name+" · "+result.Sequence.Steps.Count+" clips. See the Sequence tab for the route and any missing links.";
        }
        catch(Exception e){if(request==previewVersion)ShowPreviewError(e.Message);}
    }
    private int BeginPreview()
    {
        int request=++previewVersion;timer.Stop();play.Content="Play";sequence=null;activeStep=-1;playbackTime=0;events.Text="";sequenceDetails.Text="";
        viewport.Animation=null;viewport.EmptyTitle="Loading preview…";viewport.EmptyDetail="";viewport.InvalidateVisual();return request;
    }
    private void ShowPreviewError(string message)
    {viewport.EmptyTitle="Preview unavailable";viewport.EmptyDetail=message;viewport.InvalidateVisual();clipInfo.Text="Preview unavailable: "+message;}
    private void SetSequence(AttackSequence value)
    {
        sequence=value;activeStep=-1;sequenceDetails.Text=value.Report;
        ticking=true;timeline.Maximum=value.Duration;timeline.Value=0;ticking=false;SetPreviewTime(0);
    }
    private void SetPreviewTime(double time)
    {
        if(sequence==null)return;playbackTime=Math.Clamp(time,0,sequence.Duration);var sample=sequence.At(playbackTime);
        viewport.Animation=sample.Step.Clip;viewport.Time=sample.LocalTime;
        if(activeStep!=sample.Index)
        {
            activeStep=sample.Index;selectingPlaybackClip=true;clips.SelectedItem=sample.Step.File;selectingPlaybackClip=false;
            events.Text=sample.Step.File.RelativePath+"\n"+string.Join('\n',sample.Step.Clip.Events.Select(e=>$"{e.Time,6:0.000}  {e.Text}"));
            clipInfo.Text=$"{sequence.Name} · {sample.Index+1}/{sequence.Steps.Count} · {sample.Step.File.Name} · {sample.Step.Clip.Description}";
        }
        viewport.Caption=sequence.Name+" · "+(sample.Index+1)+"/"+sequence.Steps.Count;
        viewport.TimeLabel=$"{playbackTime:0.00} / {sequence.Duration:0.00} s"+(sequence.Steps.Count>1?$"   ·   clip {sample.LocalTime:0.00} / {sample.Step.Clip.Duration:0.00} s":"");
        ticking=true;timeline.Value=playbackTime;ticking=false;viewport.InvalidateVisual();
    }
    private void TogglePlay(){if(sequence==null)return;if(timer.IsEnabled){timer.Stop();play.Content="Play";}else{if(playbackTime>=sequence.Duration)SetPreviewTime(0);clock.Restart();lastTick=0;timer.Start();play.Content="Pause";}}
    private void Tick()
    {
        if(sequence==null)return;double now=clock.Elapsed.TotalSeconds;double delta=now-lastTick;lastTick=now;double multiplier=new[]{.25,.5,1,2}[Math.Max(0,speed.SelectedIndex)];double t=playbackTime+delta*multiplier;
        if(t>sequence.Duration){if(loop.IsChecked==true)t%=sequence.Duration;else{t=sequence.Duration;timer.Stop();play.Content="Play";}}
        SetPreviewTime(t);
    }
    private Task AddSelected()
    {
        if(sourceList.SelectedItem is not Bundle b)throw new InvalidOperationException("Select a complete source moveset first.");
        if(editorRule==null||!editorRule.SetIds.ToHashSet().SetEquals(b.Sets.Select(s=>s.Id)))throw new InvalidOperationException("Select the source moveset again to prepare its rules.");
        var rule=ReadEditor(editorRule);
        if(project.Rules.Any(r=>r.Id==rule.Id)){rule.Id=Guid.NewGuid().ToString("N")[..12];rule.Priority=WeaponDetector.Draft(rule.Name,b.Sets,project.Rules).Priority;}
        project.Rules.Add(rule);
        editorRule=rule;
        var row=new AddedSetRow(rule);
        SelectAddedSet(row, () => addedSets.Add(row));
        LoadEditor(rule);status.Text="Added the complete moveset with the displayed rules.";return Task.CompletedTask;
    }

    // Keep the row identity stable when validated editor changes replace a Rule.
    // This lets the grid update the cells without resetting selection or scrolling.
    private sealed class AddedSetRow(Rule rule) : INotifyPropertyChanged
    {
        public Rule Rule { get; private set; } = rule;
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Update(Rule updated)
        {
            Rule = updated;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Rule)));
        }
    }

    private void SelectAddedSet(AddedSetRow? row, Action? changeRows = null)
    {
        changingSelection = true;
        try
        {
            changeRows?.Invoke();
            rules.SelectedItem = row;
            if (row != null) rules.ScrollIntoView(row, null);
        }
        finally { changingSelection = false; }
    }

    private void ReloadAddedSets()
    {
        SelectAddedSet(null, () =>
        {
            addedSets.Clear();
            foreach (var rule in project.Rules) addedSets.Add(new AddedSetRow(rule));
        });
    }

    private void LoadRule()
    {
        if (changingSelection || rules.SelectedItem is not AddedSetRow row) return;
        try
        {
            CommitEditor();
            editorRule = row.Rule;
            changingSelection = true;
            sourceList.SelectedItem = bundles.FirstOrDefault(b => editorRule.SetIds.ToHashSet().SetEquals(b.Sets.Select(s => s.Id)));
            changingSelection = false;
            LoadEditor(editorRule);
            detectionInfo.Text = "Saved selection · editable rules";
            ShowSources(library.Sets.Where(s => editorRule.SetIds.Contains(s.Id)).ToList());
        }
        catch (Exception e)
        {
            changingSelection = false;
            status.Text = "Finish the current rule first: " + e.Message;
            SelectAddedSet(addedSets.FirstOrDefault(item => item.Rule.Id == editorRule?.Id));
        }
    }
    private void LoadEditor(Rule r)
    {
        loadingEditor=true;
        try{editorInfo.Text=project.Rules.Any(x=>x.Id==r.Id)?"EDITING ADDED MOVESET":"NEW SELECTION · review, then add";ruleName.Text=r.Name;weapon.SelectedItem=WeaponProfiles.ForRule(r);keyword.Text=r.WeaponKeyword;actors.SelectedItem=r.Actors;hand.SelectedItem=r.Hand;priority.Text=r.Priority.ToString();levelMin.Text=r.MinimumLevel?.ToString();levelMax.Text=r.MaximumLevel?.ToString();skillMin.Text=r.MinimumSkill?.ToString();skillMax.Text=r.MaximumSkill?.ToString();skill.SelectedIndex=Array.IndexOf(new[]{6,7,8,9},r.SkillActorValue);racePlugin.Text=r.RacePlugin;raceId.Text=r.RaceForm;weaponPlugin.Text=r.WeaponPlugin;weaponId.Text=r.WeaponForm;advanced.Text=r.ExtraConditions.ToJsonString(Json.Options);}
        finally{loadingEditor=false;}
    }
    private Rule ReadEditor(Rule original)
    {
        var r=JsonSerializer.Deserialize<Rule>(JsonSerializer.Serialize(original,Json.Options),Json.Options)!;
        int? Number(TextBox box){if(string.IsNullOrWhiteSpace(box.Text))return null;if(!int.TryParse(box.Text,out int n)||n<0)throw new InvalidDataException("Enter a nonnegative whole number or leave the field empty.");return n;}
        var extra=JsonNode.Parse(advanced.Text??"[]") as JsonArray??throw new InvalidDataException("Advanced conditions must be a JSON array.");
        var minL=Number(levelMin);var maxL=Number(levelMax);var minS=Number(skillMin);var maxS=Number(skillMax);var p=Number(priority)??throw new InvalidDataException("Priority is required.");
        if(minL>maxL||minS>maxS)throw new InvalidDataException("Minimum must not exceed maximum.");
        var profile=weapon.SelectedItem as WeaponProfile??WeaponProfiles.Source;
        r.Name=ruleName.Text??"Moveset";r.WeaponProfile=profile.Id;r.WeaponType=profile.Type??r.WeaponType;r.WeaponKeyword=keyword.Text??"";r.Actors=actors.SelectedItem?.ToString()??"Both";r.Hand=hand.SelectedItem?.ToString()??"Right";r.Priority=p;r.MinimumLevel=minL;r.MaximumLevel=maxL;r.MinimumSkill=minS;r.MaximumSkill=maxS;r.SkillActorValue=new[]{6,7,8,9}[Math.Max(0,skill.SelectedIndex)];r.RacePlugin=racePlugin.Text??"";r.RaceForm=raceId.Text??"";r.WeaponPlugin=weaponPlugin.Text??"";r.WeaponForm=weaponId.Text??"";r.ExtraConditions=extra;Conditions.For(r);return r;
    }
    private void CommitEditor()
    {
        if (editorRule == null) return;
        var updated = ReadEditor(editorRule);
        int index = project.Rules.FindIndex(rule => rule.Id == updated.Id);
        if (index >= 0)
        {
            project.Rules[index] = updated;
            addedSets[index].Update(updated);
        }
        editorRule = updated;
    }
    private Task ApplyRule(){if(editorRule==null)throw new InvalidOperationException("Select a source or added moveset first.");CommitEditor();status.Text="Rules applied.";return Task.CompletedTask;}
    private Task RemoveRule()
    {
        if (rules.SelectedItem is AddedSetRow row)
        {
            project.Rules.Remove(row.Rule);
            editorRule = null;
            SelectAddedSet(null, () => addedSets.Remove(row));
            sourceList.SelectedItem = null;
            editorInfo.Text = "Select a source moveset";
        }
        return Task.CompletedTask;
    }
    private async Task SaveProject()
    {
        CommitEditor();project.Name=projectName.Text??"My Combat Movesets";project.SkeletonPath=skeleton.Text??"";
        var p=await StorageProvider.SaveFilePickerAsync(new(){Title="Save moveset project",SuggestedFileName=Paths.Slug(project.Name)+".json",DefaultExtension="json"});if(p?.TryGetLocalPath() is {} path){Json.Write(path,project);status.Text="Saved "+path;}
    }
    private async Task OpenProject()
    {
        if(await PickFile("Open moveset project","*.json") is not {} path)return;
        var saved = JsonSerializer.Deserialize<Project>(File.ReadAllText(path), Json.Options)
            ?? throw new InvalidDataException("Empty project.");
        if (saved.Schema != 1) throw new InvalidDataException("Unsupported project schema.");
        editorRule = null;
        sourceList.SelectedItem = null;
        project = saved;
        ReloadAddedSets();
        editorInfo.Text = "Select a source or added moveset";
        projectName.Text = saved.Name;
        skeleton.Text = saved.SkeletonPath;
        loadingEditor = true;
        target.SelectedIndex = saved.TargetFramework == "OAR" ? 1 : 0;
        loadingEditor = false;
        source.Text = saved.SourceRoots.FirstOrDefault() ?? "";
        extraRoots.Clear();
        extraRoots.AddRange(saved.SourceRoots.Skip(1));
        RefreshProfiles();
        restoringProject = true;
        try { await Scan(); }
        finally { restoringProject = false; }
    }
    private Task NewProject(){editorRule=null;project=new(){SourceRoots=library.Roots.ToList(),ApplyProfileOverrides=project.ApplyProfileOverrides};target.SelectedIndex=0;projectName.Text=project.Name;ReloadAddedSets();sourceList.SelectedItem=null;editorInfo.Text="Select a source moveset";status.Text="New output project. Library retained.";return Task.CompletedTask;}
    private async Task ImportArchive(){if(await PickFile("Import animation archive","*.zip","*.bsa") is {} p){status.Text="Importing archive…";var folder=await Task.Run(()=>Import.Archive(p));if(string.IsNullOrWhiteSpace(source.Text))source.Text=folder;else extraRoots.Add(folder);await Scan();}}
    private async Task Export()
    {
        CommitEditor();project.Name=projectName.Text??"My Combat Movesets";project.SkeletonPath=skeleton.Text??"";
        var issues=Exporter.Validate(project,library);var errors=issues.Where(i=>i.Severity=="Error").ToList();if(errors.Count>0)throw new InvalidDataException(string.Join("\n",errors.Select(e=>e.Message)));
        var report=new Window{Title="Review mod export",Width=700,Height=530,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var text=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Text=$"{project.Rules.Count} complete moveset selections\n\n"+string.Join('\n',issues.Select(i=>i.Severity+": "+i.Message))+"\n\nAnimation bytes, annotations, timing and movement stay unchanged. Source gameplay dependencies remain required."};
        var export=new Button{Content="Choose output folder and build",HorizontalAlignment=HorizontalAlignment.Right};export.Click+=(_,_)=>report.Close(true);var grid=new Grid{RowDefinitions=new("*,Auto"),Margin=new Thickness(16),RowSpacing=12};grid.Children.Add(text);Put(grid,export,row:1);report.Content=grid;
        if(await report.ShowDialog<bool>(this)!=true)return;if(await PickFolder("Choose output folder for the installable ZIP") is not {} parent)return;
        status.Text="Building mod…";var result=await Task.Run(()=>Exporter.Build(project,library,parent));status.Text=$"Exported {result.AnimationCount:N0} unchanged animation files: {result.Archive}";
    }
    private async Task Initialize()
    {
        string? Arg(string key){int at=Array.IndexOf(args,key);return at>=0&&at+1<args.Length?args[at+1]:null;}
        if(Arg("--source") is {} p){source.Text=p;RefreshProfiles();}
        if(Arg("--profile") is {} profile)profiles.SelectedIndex=profileChoices.FindIndex(p=>p.Name.Equals(profile,StringComparison.OrdinalIgnoreCase))+1;
        if(Arg("--target")=="oar")target.SelectedIndex=1;
        if(args.Contains("--all"))combatOnly.IsChecked=false;
        if(Arg("--skeleton") is {} sk)skeleton.Text=sk;
        if(!string.IsNullOrWhiteSpace(source.Text))await Scan();
        if(Arg("--select") is {} select){var item=(sourceList.ItemsSource as IEnumerable<Bundle>)?.FirstOrDefault(b=>b.Name.Contains(select,StringComparison.OrdinalIgnoreCase));if(item!=null){sourceList.SelectedItem=item;await LoadPreview();}}
        if(Arg("--draft-name") is {} draftName)ruleName.Text=draftName;
        if(Arg("--draft-level") is {} draftLevel)levelMin.Text=draftLevel;
        if(args.Contains("--add-selected"))await AddSelected();
        if(Arg("--select-next") is {} next){var item=(sourceList.ItemsSource as IEnumerable<Bundle>)?.FirstOrDefault(b=>b.Name.Contains(next,StringComparison.OrdinalIgnoreCase));if(item!=null){sourceList.SelectedItem=item;await LoadPreview();}}
        if(Arg("--preview-folder") is {} folder && previewFolders.ItemsSource is IEnumerable<PreviewFolder> options){previewFolders.SelectedItem=options.FirstOrDefault(p=>p.Set.Folder.Contains(folder,StringComparison.OrdinalIgnoreCase));await LoadPreview();}
        if(Arg("--order")=="numbered")sequenceOrder.SelectedIndex=1;
        if(args.Contains("--no-loop"))loop.IsChecked=false;
        if(Arg("--chain") is {} chain)await LoadChain(chain.Equals("power",StringComparison.OrdinalIgnoreCase)?AttackKind.Power:AttackKind.Normal);
        if(double.TryParse(Arg("--play-seconds"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double seconds)&&seconds>0&&seconds<=30&&sequence!=null){if(!timer.IsEnabled)TogglePlay();await Task.Delay(TimeSpan.FromSeconds(seconds));}
        if(Arg("--snapshot") is {} imagePath)
        {
            timer.Stop();play.Content="Play";
            if(double.TryParse(Arg("--time"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double time))SetPreviewTime(time);
            await Task.Delay(600);var bitmap=new RenderTargetBitmap(new PixelSize((int)Bounds.Width,(int)Bounds.Height),new Vector(96,96));bitmap.Render(this);bitmap.Save(imagePath);
            if(Arg("--snapshot-metadata") is {} metadata)Json.Write(metadata,new{preview=sequence?.Name,error=sequence==null?clipInfo.Text:null,time=playbackTime,activeStep,folder=(previewFolders.SelectedItem as PreviewFolder)?.Set.Folder,steps=sequence?.Steps.Select(s=>new{file=s.File.RelativePath,s.Start,s.Length}),notes=sequence?.Notes,editor=new{name=ruleName.Text,profile=(weapon.SelectedItem as WeaponProfile)?.Id,keyword=keyword.Text,hand=hand.SelectedItem,actors=actors.SelectedItem,level=levelMin.Text,priority=priority.Text,detection=detectionInfo.Text},project.TargetFramework,project.ApplyProfileOverrides,project.Rules});
            Close();
        }
    }
}
