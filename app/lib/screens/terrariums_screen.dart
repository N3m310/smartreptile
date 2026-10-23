import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../core/permissions.dart';
import '../core/problem.dart';
import '../l10n/generated/app_localizations.dart';
import '../models/api_terrarium_models.dart';
import '../state/session_provider.dart';
import '../state/terrarium_provider.dart';
import '../theme/app_theme.dart';
import '../l10n/labels.dart';
import 'terrarium_detail_screen.dart';

class TerrariumsScreen extends StatefulWidget {
  const TerrariumsScreen({super.key});

  @override
  State<TerrariumsScreen> createState() => _TerrariumsScreenState();
}

class _TerrariumsScreenState extends State<TerrariumsScreen> {
  final _searchController = TextEditingController();
  String _searchQuery = '';

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      context.read<TerrariumProvider>().loadTerrariums();
    });
  }

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  Future<void> _createTerrarium() async {
    final created = await showModalBottomSheet<ApiTerrarium>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      backgroundColor: AppColors.bgCard,
      builder: (_) => const _CreateTerrariumSheet(),
    );
    if (!mounted || created == null) return;
    await context.read<TerrariumProvider>().loadTerrariums();
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(AppLocalizations.of(context).terrariumCreated),
        backgroundColor: AppColors.primary,
      ),
    );
    await Navigator.push(
      context,
      MaterialPageRoute(
        builder: (_) => TerrariumDetailScreen(terrariumId: created.id),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final data = context.watch<TerrariumProvider>();
    final session = context.watch<SessionProvider>();
    final l10n = AppLocalizations.of(context);
    final isAdmin = (session.user?.role.toLowerCase() == 'administrator') ||
        (session.user?.role.toLowerCase() == 'admin') ||
        session.hasPermission(Permissions.dataReadAny) ||
        session.hasPermission(Permissions.userRead);

    final filtered = isAdmin && _searchQuery.isNotEmpty
        ? data.terrariums.where((t) {
            final q = _searchQuery.toLowerCase();
            return t.name.toLowerCase().contains(q) ||
                t.speciesName.toLowerCase().contains(q) ||
                (t.description?.toLowerCase().contains(q) ?? false) ||
                (t.location?.toLowerCase().contains(q) ?? false);
          }).toList()
        : data.terrariums;

    return Scaffold(
      backgroundColor: AppColors.bgMain,
      floatingActionButton: isAdmin
          ? null
          : FloatingActionButton.extended(
              backgroundColor: AppColors.primary,
              foregroundColor: Colors.white,
              onPressed: _createTerrarium,
              icon: const Icon(Icons.add),
              label: Text(l10n.terrariumNew),
            ),
      body: RefreshIndicator(
        onRefresh: data.loadTerrariums,
        child: data.isLoading && data.terrariums.isEmpty
            ? const Center(child: CircularProgressIndicator())
            : data.lastFailure != null && data.terrariums.isEmpty
            ? ListView(
                padding: const EdgeInsets.all(24),
                children: [
                  Text(
                    failureMessage(l10n, data.lastFailure!),
                    textAlign: TextAlign.center,
                    style: const TextStyle(color: AppColors.statusDanger),
                  ),
                  TextButton(
                    onPressed: data.loadTerrariums,
                    child: Text(l10n.retry),
                  ),
                ],
              )
            : isAdmin
            ? ListView(
                padding: const EdgeInsets.all(16),
                children: [
                  // Summary row (format matches Devices screen Image 2)
                  Row(
                    children: [
                      Expanded(
                        child: Container(
                          padding: const EdgeInsets.symmetric(
                            horizontal: 14,
                            vertical: 12,
                          ),
                          decoration: BoxDecoration(
                            color: AppColors.bgCard,
                            borderRadius: BorderRadius.circular(14),
                            border: Border.all(color: AppColors.border),
                          ),
                          child: Row(
                            mainAxisAlignment: MainAxisAlignment.spaceBetween,
                            children: [
                              const Text(
                                'Tá»•ng bá»ƒ nuĂ´i:',
                                style: TextStyle(
                                  color: AppColors.textMuted,
                                  fontSize: 13,
                                ),
                              ),
                              Text(
                                '${data.terrariums.length}',
                                style: const TextStyle(
                                  color: AppColors.textMain,
                                  fontWeight: FontWeight.bold,
                                  fontSize: 15,
                                ),
                              ),
                            ],
                          ),
                        ),
                      ),
                      const SizedBox(width: 10),
                      ElevatedButton.icon(
                        style: ElevatedButton.styleFrom(
                          backgroundColor: AppColors.primary,
                          foregroundColor: Colors.white,
                          padding: const EdgeInsets.symmetric(
                            horizontal: 14,
                            vertical: 12,
                          ),
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(14),
                          ),
                        ),
                        onPressed: _createTerrarium,
                        icon: const Icon(Icons.add, size: 18),
                        label: Text(
                          l10n.terrariumNew,
                          style: const TextStyle(fontSize: 13),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 12),

                  // Search bar
                  TextField(
                    controller: _searchController,
                    style: const TextStyle(
                      color: AppColors.textMain,
                      fontSize: 13,
                    ),
                    decoration: InputDecoration(
                      hintText: 'TĂ¬m theo tĂªn bá»ƒ nuĂ´i, loĂ i, vá»‹ trĂ­...',
                      hintStyle: const TextStyle(
                        color: AppColors.textMuted,
                        fontSize: 13,
                      ),
                      prefixIcon: const Icon(
                        Icons.search,
                        color: AppColors.textMuted,
                        size: 20,
                      ),
                      suffixIcon: _searchController.text.isNotEmpty
                          ? IconButton(
                              icon: const Icon(
                                Icons.clear,
                                size: 18,
                                color: AppColors.textMuted,
                              ),
                              onPressed: () {
                                _searchController.clear();
                                setState(() => _searchQuery = '');
                              },
                            )
                          : null,
                      filled: true,
                      fillColor: AppColors.bgInput,
                      contentPadding: const EdgeInsets.symmetric(vertical: 10),
                      border: OutlineInputBorder(
                        borderRadius: BorderRadius.circular(14),
                        borderSide: const BorderSide(color: AppColors.border),
                      ),
                      enabledBorder: OutlineInputBorder(
                        borderRadius: BorderRadius.circular(14),
                        borderSide: const BorderSide(color: AppColors.border),
                      ),
                      focusedBorder: OutlineInputBorder(
                        borderRadius: BorderRadius.circular(14),
                        borderSide: const BorderSide(color: AppColors.primary),
                      ),
                    ),
                    onChanged: (val) => setState(
                      () => _searchQuery = val.trim().toLowerCase(),
                    ),
                  ),
                  const SizedBox(height: 14),

                  if (filtered.isEmpty) ...[
                    const SizedBox(height: 32),
                    const Center(
                      child: Column(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Icon(
                            Icons.search_off,
                            size: 40,
                            color: AppColors.textMuted,
                          ),
                          SizedBox(height: 10),
                          Text(
                            'KhĂ´ng tĂ¬m tháº¥y bá»ƒ nuĂ´i phĂ¹ há»£p',
                            style: TextStyle(
                              color: AppColors.textMuted,
                              fontSize: 14,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ] else
                    for (final item in filtered)
                      _TerrariumCard(item: item),
                ],
              )
            : data.terrariums.isEmpty
            ? ListView(
                padding: const EdgeInsets.all(24),
                children: [
                  const Icon(Icons.pets, size: 48, color: AppColors.primary),
                  const SizedBox(height: 12),
                  Text(l10n.emptyStateTitle, textAlign: TextAlign.center),
                  const SizedBox(height: 6),
                  Text(l10n.emptyStateBody, textAlign: TextAlign.center),
                ],
              )
            : ListView.builder(
                padding: const EdgeInsets.all(16),
                itemCount: data.terrariums.length,
                itemBuilder: (context, index) {
                  final item = data.terrariums[index];
                  return _TerrariumCard(item: item);
                },
              ),
      ),
    );
  }
}

class _TerrariumCard extends StatelessWidget {
  const _TerrariumCard({required this.item});

  final ApiTerrarium item;

  @override
  Widget build(BuildContext context) {
    return Card(
      margin: const EdgeInsets.only(bottom: 14),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: () => Navigator.push(
          context,
          MaterialPageRoute(
            builder: (_) => TerrariumDetailScreen(terrariumId: item.id),
          ),
        ),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Row(
            children: [
              Container(
                width: 58,
                height: 58,
                decoration: BoxDecoration(
                  color: AppColors.bgCardHover,
                  borderRadius: BorderRadius.circular(14),
                ),
                child: const Icon(
                  Icons.pets,
                  color: AppColors.primary,
                  size: 28,
                ),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      item.name,
                      style: const TextStyle(
                        color: AppColors.textMain,
                        fontSize: 16,
                        fontWeight: FontWeight.bold,
                      ),
                    ),
                    const SizedBox(height: 3),
                    Text(
                      item.speciesName,
                      style: const TextStyle(
                        color: AppColors.accent,
                        fontSize: 12,
                      ),
                    ),
                    if (item.location?.isNotEmpty == true) ...[
                      const SizedBox(height: 6),
                      Text(
                        item.location!,
                        style: const TextStyle(
                          color: AppColors.textMuted,
                          fontSize: 11,
                        ),
                      ),
                    ],
                    // Set only on an administrator's answers, whose list can hold rows belonging to accounts they
                    // did not name.
                    if (item.ownerEmail != null) ...[
                      const SizedBox(height: 6),
                      Row(
                        children: [
                          const Icon(
                            Icons.person_outline,
                            size: 13,
                            color: AppColors.textMuted,
                          ),
                          const SizedBox(width: 4),
                          Expanded(
                            child: Text(
                              item.ownerEmail!,
                              style: const TextStyle(
                                color: AppColors.textMuted,
                                fontSize: 11,
                              ),
                              overflow: TextOverflow.ellipsis,
                            ),
                          ),
                        ],
                      ),
                    ],
                  ],
                ),
              ),
              const Icon(
                Icons.chevron_right,
                color: AppColors.textMuted,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _CreateTerrariumSheet extends StatefulWidget {
  const _CreateTerrariumSheet();

  @override
  State<_CreateTerrariumSheet> createState() => _CreateTerrariumSheetState();
}

class _CreateTerrariumSheetState extends State<_CreateTerrariumSheet> {
  final _formKey = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _location = TextEditingController();
  List<ApiSpeciesProfile> _profiles = [];
  ApiSpeciesProfile? _selected;
  AppFailure? _failure;
  bool _loadingProfiles = true;
  bool _saving = false;

  @override
  void initState() {
    super.initState();
    _loadProfiles();
  }

  Future<void> _loadProfiles() async {
    try {
      final profiles = await context
          .read<TerrariumProvider>()
          .loadSpeciesProfiles();
      if (!mounted) return;
      setState(() {
        _profiles = profiles;
        _selected = profiles.isEmpty ? null : profiles.first;
        _loadingProfiles = false;
      });
    } on AppFailure catch (failure) {
      if (!mounted) return;
      setState(() {
        _failure = failure;
        _loadingProfiles = false;
      });
    }
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate() || _selected == null) return;
    setState(() {
      _failure = null;
      _saving = true;
    });
    try {
      final created = await context.read<TerrariumProvider>().createTerrarium(
        name: _name.text,
        speciesProfileId: _selected!.id,
        location: _location.text,
      );
      if (mounted) Navigator.pop(context, created);
    } on AppFailure catch (failure) {
      if (mounted) setState(() => _failure = failure);
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  void dispose() {
    _name.dispose();
    _location.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Padding(
      padding: EdgeInsets.fromLTRB(
        20,
        20,
        20,
        MediaQuery.of(context).viewInsets.bottom + 20,
      ),
      child: Form(
        key: _formKey,
        child: ListView(
          shrinkWrap: true,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    l10n.terrariumNew,
                    style: const TextStyle(
                      color: AppColors.textMain,
                      fontWeight: FontWeight.bold,
                      fontSize: 18,
                    ),
                  ),
                ),
                IconButton(
                  onPressed: () => Navigator.pop(context),
                  icon: const Icon(Icons.close),
                ),
              ],
            ),
            const SizedBox(height: 12),
            TextFormField(
              controller: _name,
              maxLength: 60,
              decoration: InputDecoration(labelText: l10n.terrariumName),
              validator: (value) => value == null || value.trim().isEmpty
                  ? l10n.terrariumNameRequired
                  : value.trim().length > 60
                  ? l10n.terrariumNameTooLong
                  : null,
            ),
            const SizedBox(height: 10),
            if (_loadingProfiles)
              const Center(child: CircularProgressIndicator())
            else if (_failure != null)
              Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    failureMessage(l10n, _failure!),
                    style: const TextStyle(color: AppColors.statusDanger),
                  ),
                  TextButton(onPressed: _loadProfiles, child: Text(l10n.retry)),
                ],
              )
            else
              DropdownButtonFormField<ApiSpeciesProfile>(
                initialValue: _selected,
                isExpanded: true,
                decoration: InputDecoration(labelText: l10n.terrariumSpecies),
                items: _profiles
                    .map(
                      (profile) => DropdownMenuItem(
                        value: profile,
                        child: Row(
                          children: [
                            _ProfileThumbnail(profile: profile),
                            const SizedBox(width: 10),
                            Expanded(
                              child: Column(
                                mainAxisSize: MainAxisSize.min,
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(
                                    profile.name,
                                    overflow: TextOverflow.ellipsis,
                                  ),
                                  if (profile.scientificName.isNotEmpty)
                                    Text(
                                      profile.scientificName,
                                      overflow: TextOverflow.ellipsis,
                                      style: const TextStyle(
                                        color: AppColors.textMuted,
                                        fontSize: 11,
                                        fontStyle: FontStyle.italic,
                                      ),
                                    ),
                                ],
                              ),
                            ),
                          ],
                        ),
                      ),
                    )
                    .toList(),
                onChanged: (profile) => setState(() => _selected = profile),
                validator: (profile) =>
                    profile == null ? l10n.speciesProfileChoose : null,
              ),
            if (_selected != null) ...[
              const SizedBox(height: 8),
              Align(
                alignment: Alignment.centerLeft,
                child: TextButton.icon(
                  onPressed: () => _showProfileDetails(context, _selected!),
                  icon: const Icon(Icons.info_outline),
                  label: Text(l10n.speciesProfileDetail),
                ),
              ),
            ],
            TextFormField(
              controller: _location,
              maxLength: 120,
              decoration: InputDecoration(labelText: l10n.terrariumLocation),
              validator: (value) => (value?.length ?? 0) > 120
                  ? l10n.terrariumLocationTooLong
                  : null,
            ),
            if (_failure != null && !_loadingProfiles)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Text(
                  failureMessage(l10n, _failure!),
                  style: const TextStyle(color: AppColors.statusDanger),
                ),
              ),
            const SizedBox(height: 18),
            SizedBox(
              height: 48,
              child: ElevatedButton(
                onPressed: _saving || _loadingProfiles || _selected == null
                    ? null
                    : _submit,
                child: Text(_saving ? l10n.creating : l10n.create),
              ),
            ),
          ],
        ),
      ),
    );
  }

  void _showProfileDetails(BuildContext context, ApiSpeciesProfile profile) {
    final l10n = AppLocalizations.of(context);
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      backgroundColor: AppColors.bgCard,
      builder: (context) => DraggableScrollableSheet(
        expand: false,
        initialChildSize: 0.72,
        builder: (context, controller) => ListView(
          controller: controller,
          padding: const EdgeInsets.all(20),
          children: [
            Text(
              profile.name,
              style: const TextStyle(fontSize: 20, fontWeight: FontWeight.bold),
            ),
            if (profile.scientificName.isNotEmpty)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text(
                  profile.scientificName,
                  style: const TextStyle(
                    fontStyle: FontStyle.italic,
                    color: AppColors.textMuted,
                  ),
                ),
              ),
            if (profile.imageUrl != null)
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 14),
                child: _SpeciesPicture(profile: profile),
              ),
            const SizedBox(height: 12),
            Text(
              '${l10n.profilePhotoperiodHours}: ${profile.photoperiodHours}',
            ),
            Text('${l10n.profileLightsOn}: ${profile.lightsOnLocalTime}'),
            Text(
              '${l10n.profileLightThreshold}: ${profile.lightThresholdLux} lx',
            ),
            Text(
              '${l10n.profileMinLightHours}: ${profile.minLightHoursPerDay}',
            ),
            const SizedBox(height: 18),
            Text(
              l10n.speciesProfileBands,
              style: const TextStyle(fontWeight: FontWeight.bold),
            ),
            for (final band in profile.bands)
              Card(
                child: ListTile(
                  title: Text('${band['metric']} Â· ${band['phase']}'),
                  subtitle: Text(
                    '${band['targetMin'] ?? 'â€”'}â€“${band['targetMax'] ?? 'â€”'} ${band['unit'] ?? ''} Â· ${l10n.thresholdColumnCritical}: ${band['criticalMin'] ?? 'â€”'}â€“${band['criticalMax'] ?? 'â€”'}',
                  ),
                ),
              ),
            if (profile.notes?.isNotEmpty == true) Text(profile.notes!),
          ],
        ),
      ),
    );
  }
}

class _SpeciesPicture extends StatelessWidget {
  const _SpeciesPicture({required this.profile});

  final ApiSpeciesProfile profile;

  @override
  Widget build(BuildContext context) {
    return FutureBuilder(
      future: context.read<TerrariumProvider>().loadProfileImage(profile),
      builder: (context, snapshot) {
        if (snapshot.connectionState == ConnectionState.waiting) {
          return const SizedBox(
            height: 140,
            child: Center(child: CircularProgressIndicator()),
          );
        }
        if (snapshot.hasError || snapshot.data == null) {
          return const SizedBox(
            height: 140,
            child: Center(child: Icon(Icons.broken_image_outlined, size: 36)),
          );
        }
        return ClipRRect(
          borderRadius: BorderRadius.circular(14),
          child: Image.memory(
            snapshot.data!,
            height: 180,
            width: double.infinity,
            fit: BoxFit.cover,
            errorBuilder: (_, _, _) => const SizedBox.shrink(),
          ),
        );
      },
    );
  }
}

class _ProfileThumbnail extends StatelessWidget {
  const _ProfileThumbnail({required this.profile});

  final ApiSpeciesProfile profile;

  @override
  Widget build(BuildContext context) {
    if (profile.imageUrl == null) {
      return const CircleAvatar(radius: 22, child: Icon(Icons.pets, size: 18));
    }
    return FutureBuilder(
      future: context.read<TerrariumProvider>().loadProfileImage(profile),
      builder: (context, snapshot) {
        if (snapshot.connectionState == ConnectionState.waiting) {
          return const CircleAvatar(
            radius: 22,
            child: SizedBox(
              height: 16,
              width: 16,
              child: CircularProgressIndicator(strokeWidth: 2),
            ),
          );
        }
        if (snapshot.hasError || snapshot.data == null) {
          return const CircleAvatar(
            radius: 22,
            child: Icon(Icons.broken_image_outlined, size: 18),
          );
        }
        return CircleAvatar(
          radius: 22,
          backgroundImage: MemoryImage(snapshot.data!),
          onBackgroundImageError: (_, _) {},
        );
      },
    );
  }
}
